using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using RustPlusApi;
using RustPlusApi.Data.Entities;
using RustPlusApi.Fcm;
using RustPlusApi.Fcm.Data;
using RustPlusApi.Fcm.Data.Events;
using RustPlusApi.Fcm.Registration;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class RustPlusConnectionService : IDisposable
{
    private const int PersistentIdLimit = 1000;

    private readonly RustPlusProfileStore _store;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _companionOperationGate = new(1, 1);
    private readonly object _profileGate = new();
    private RustPlusStoredProfile? _profile;
    private RustPlusFcm? _listener;
    private volatile bool _listenerConnected;
    private bool _disposed;

    public RustPlusConnectionService(RustPlusProfileStore store)
    {
        _store = store;
    }

    public string Status { get; private set; } = "Rust+ is not connected.";
    public bool IsListenerConnected => _listenerConnected;

    public ulong? SteamId
    {
        get
        {
            lock (_profileGate)
            {
                return _profile is { SteamId: > 0 } profile ? profile.SteamId : null;
            }
        }
    }

    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? AlarmTriggered;
    public event Action<RustPlusServerProfile>? ServerPaired;
    public event Action<RustPlusDeviceProfile>? EntityPaired;

    public IReadOnlyList<RustPlusServerProfile> GetServers()
    {
        lock (_profileGate)
        {
            return _profile?.Servers.ToArray() ?? [];
        }
    }

    public IReadOnlyList<RustPlusDeviceProfile> GetDevices(Guid serverId)
    {
        lock (_profileGate)
        {
            return _profile?.Devices.Where(device => device.ServerId == serverId).ToArray() ?? [];
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_profileGate)
            {
                _profile = _store.Load();
            }

            if (string.IsNullOrWhiteSpace(_profile?.CredentialsJson)
                || string.IsNullOrWhiteSpace(_profile.SteamAuthToken))
            {
                SetStatus(string.IsNullOrWhiteSpace(_profile?.CredentialsJson)
                    ? "Rust+ is not connected."
                    : "Rust+ setup is incomplete. Select Connect Rust+ to finish Steam sign-in.");
                return;
            }

            var credentials = CredentialsStore.Deserialize(_profile.CredentialsJson);
            await StartListenerAsync(credentials, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_profileGate)
            {
                _profile ??= _store.Load() ?? new RustPlusStoredProfile();
            }

            var credentials = string.IsNullOrWhiteSpace(_profile.CredentialsJson)
                ? null
                : CredentialsStore.Deserialize(_profile.CredentialsJson);

            if (credentials is null)
            {
                SetStatus("Registering this device with Rust+…");
                var registration = new FcmRegistration();
                credentials = await registration.AcquireCredentialsAsync(cancellationToken);
                _profile.CredentialsJson = CredentialsStore.Serialize(credentials);
                SaveProfile();
            }

            if (string.IsNullOrWhiteSpace(_profile.SteamAuthToken))
            {
                SetStatus("Sign in to Steam in the browser window to finish Rust+ setup…");
                var registration = new FcmRegistration();
                var login = await registration.RegisterWithRustPlusAsync(
                    credentials,
                    onLoginUrl: _ => SetStatus("Complete Steam sign-in in the browser window…"),
                    cancellationToken);
                _profile.SteamAuthToken = login.Token;
                _profile.SteamId = login.SteamId;
                SaveProfile();
            }

            if (_listenerConnected)
            {
                SetStatus("Rust+ is already listening for events.");
                return;
            }

            await StartListenerAsync(credentials, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            await _companionOperationGate.WaitAsync(cancellationToken);
            try
            {
                DisposeListener();
                lock (_profileGate)
                {
                    _store.Delete();
                    _profile = null;
                }

                SetStatus("Rust+ is disconnected.");
            }
            finally
            {
                _companionOperationGate.Release();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<RustPlusMapSnapshot> LoadMapAsync(
        RustPlusServerProfile server,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(server);
        if (server.ServerId == Guid.Empty)
        {
            throw new InvalidOperationException("This paired server does not have a Rust+ server ID.");
        }

        await _companionOperationGate.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateCompanionClient(server.ServerId, server.PlayerId, server.PlayerToken);
            await client.ConnectAsync(cancellationToken);

            var info = await client.GetInfoAsync(cancellationToken);
            if (!info.IsSuccess || info.Data is null)
            {
                throw new InvalidOperationException($"Could not load server information: {info.Error?.ToString() ?? "unknown server error"}");
            }

            if (info.Data.MapSize is not > 0)
            {
                throw new InvalidOperationException("The server returned an invalid map size.");
            }

            var mapResponse = await client.GetMapAsync(cancellationToken);
            if (!mapResponse.IsSuccess || mapResponse.Data is null || mapResponse.Data.JpgImage is not { Length: > 0 })
            {
                throw new InvalidOperationException($"Could not load the server map: {mapResponse.Error?.ToString() ?? "the server returned no map image"}");
            }

            var markersResponse = await client.GetMapMarkersAsync(cancellationToken);
            if (!markersResponse.IsSuccess || markersResponse.Data is null)
            {
                throw new InvalidOperationException($"Could not load map markers: {markersResponse.Error?.ToString() ?? "unknown server error"}");
            }

            return new RustPlusMapSnapshot(mapResponse.Data, markersResponse.Data, info.Data.MapSize.Value);
        }
        finally
        {
            _companionOperationGate.Release();
        }
    }

    public async Task<RustPlusDeviceState> LoadDeviceStateAsync(
        RustPlusDeviceProfile device,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);
        await _companionOperationGate.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateCompanionClient(device.ServerId, device.PlayerId, device.PlayerToken);
            await client.ConnectAsync(cancellationToken);

            if (string.Equals(device.EntityType, "StorageMonitor", StringComparison.OrdinalIgnoreCase))
            {
                var response = await client.GetStorageMonitorInfoAsync(device.EntityId, cancellationToken);
                if (!response.IsSuccess || response.Data is null)
                {
                    throw new InvalidOperationException($"Could not read the storage monitor: {response.Error?.ToString() ?? "unknown server error"}");
                }

                var stacks = response.Data.Items?.Count() ?? 0;
                var capacity = response.Data.Capacity?.ToString() ?? "unknown";
                var protection = response.Data.HasProtection == true ? "protected" : "not protected";
                return new RustPlusDeviceState($"{stacks} item stacks · capacity {capacity} · {protection}", null);
            }

            if (!string.Equals(device.EntityType, "Switch", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(device.EntityType, "Alarm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported Rust+ device type: {device.EntityType}.");
            }

            var deviceResponse = await client.GetSmartDeviceInfoAsync(device.EntityId, cancellationToken);
            if (!deviceResponse.IsSuccess || deviceResponse.Data is null)
            {
                throw new InvalidOperationException($"Could not read the smart device: {deviceResponse.Error?.ToString() ?? "unknown server error"}");
            }

            var state = deviceResponse.Data.IsActive;
            return new RustPlusDeviceState(state ? "On / triggered" : "Off / idle", state);
        }
        finally
        {
            _companionOperationGate.Release();
        }
    }

    public async Task<RustPlusDeviceState> SetSmartSwitchValueAsync(
        RustPlusDeviceProfile device,
        bool value,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(device);
        if (!string.Equals(device.EntityType, "Switch", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only paired Smart Switches can be controlled from this app.");
        }

        await _companionOperationGate.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateCompanionClient(device.ServerId, device.PlayerId, device.PlayerToken);
            await client.ConnectAsync(cancellationToken);
            var response = await client.SetSmartSwitchValueAsync(device.EntityId, value, cancellationToken);
            if (!response.IsSuccess || response.Data is null)
            {
                throw new InvalidOperationException($"Could not update the smart switch: {response.Error?.ToString() ?? "unknown server error"}");
            }

            var state = response.Data.IsActive;
            return new RustPlusDeviceState(state ? "On / triggered" : "Off / idle", state);
        }
        finally
        {
            _companionOperationGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeListener();
        _disposed = true;
    }

    private async Task StartListenerAsync(Credentials credentials, CancellationToken cancellationToken)
    {
        DisposeListener();
        RustPlusStoredProfile profile;
        lock (_profileGate)
        {
            profile = _profile ?? throw new InvalidOperationException("Rust+ credentials have not been initialized.");
        }

        var listener = new RustPlusFcm(credentials, profile.PersistentIds);
        listener.Connected += Listener_Connected;
        listener.Disconnected += Listener_Disconnected;
        listener.ErrorOccurred += Listener_ErrorOccurred;
        listener.PersistentIdReceived += Listener_PersistentIdReceived;
        listener.OnAlarmTriggered += Listener_AlarmTriggered;
        listener.OnServerPairing += Listener_ServerPairing;
        listener.OnEntityPairing += Listener_EntityPairing;
        _listener = listener;

        SetStatus("Connecting to Rust+ notifications…");
        var connected = false;
        try
        {
            await listener.ConnectAsync(cancellationToken);
            connected = true;
            SetStatus(GetListeningStatus());
        }
        finally
        {
            if (!connected)
            {
                DisposeListener();
            }
        }
    }

    private void Listener_Connected(object? sender, EventArgs e)
    {
        _listenerConnected = true;
        SetStatus(GetListeningStatus());
    }

    private void Listener_Disconnected(object? sender, EventArgs e)
    {
        _listenerConnected = false;
        SetStatus("Rust+ notifications disconnected. Select Reconnect to retry.");
    }

    private void Listener_ErrorOccurred(object? sender, Exception exception)
    {
        _listenerConnected = false;
        SetStatus("Rust+ notifications stopped.");
        ErrorOccurred?.Invoke($"Rust+ notification connection failed: {exception.Message}");
    }

    private void Listener_PersistentIdReceived(object? sender, string persistentId)
    {
        try
        {
            lock (_profileGate)
            {
                if (_profile is null)
                {
                    return;
                }

                _profile.PersistentIds.Add(persistentId);
                while (_profile.PersistentIds.Count > PersistentIdLimit)
                {
                    _profile.PersistentIds.Remove(_profile.PersistentIds.First());
                }

                _store.Save(_profile);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ErrorOccurred?.Invoke($"Could not save Rust+ notification state: {exception.Message}");
        }
    }

    private void Listener_AlarmTriggered(object? sender, AlarmNotification? alarm)
    {
        var details = !string.IsNullOrWhiteSpace(alarm?.Message)
            ? alarm.Message
            : !string.IsNullOrWhiteSpace(alarm?.Title)
                ? alarm.Title
                : "Rust+ Smart Alarm";
        var reason = $"Rust+ Smart Alarm: {details}";
        AlarmTriggered?.Invoke(reason.Length > 200 ? reason[..200] : reason);
    }

    private void Listener_ServerPairing(object? sender, Notification<ServerEvent?> notification)
    {
        var server = notification.Data;
        if (server is null || string.IsNullOrWhiteSpace(server.Ip) || server.Port is < 1 or > 65535)
        {
            ErrorOccurred?.Invoke("Rust+ sent incomplete server-pairing data; the server was not saved.");
            return;
        }

        RustPlusServerProfile pairedServer;
        try
        {
            lock (_profileGate)
            {
                if (_profile is null || notification.PlayerId != _profile.SteamId)
                {
                    ErrorOccurred?.Invoke("The server pairing belongs to a different Steam account and was ignored.");
                    return;
                }

                pairedServer = new RustPlusServerProfile
                {
                    ServerId = notification.ServerId,
                    Name = string.IsNullOrWhiteSpace(server.Name) ? server.Ip : server.Name,
                    Ip = server.Ip,
                    Port = server.Port,
                    PlayerId = notification.PlayerId,
                    PlayerToken = notification.PlayerToken,
                };
                _profile.Servers.RemoveAll(existing =>
                    existing.ServerId != Guid.Empty && existing.ServerId == pairedServer.ServerId
                    || existing.Ip == pairedServer.Ip && existing.Port == pairedServer.Port);
                _profile.Servers.Add(pairedServer);
                _store.Save(_profile);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ErrorOccurred?.Invoke($"Could not securely save the paired server: {exception.Message}");
            return;
        }

        ServerPaired?.Invoke(pairedServer);
        SetStatus($"Paired with {pairedServer.Name}. Rust+ notifications are active.");
    }

    private void Listener_EntityPairing(object? sender, Notification<EntityEvent?> notification)
    {
        var entity = notification.Data;
        if (entity is null || entity.EntityId is not > 0 || notification.ServerId == Guid.Empty)
        {
            ErrorOccurred?.Invoke("Rust+ sent incomplete smart-device pairing data; the device was not saved.");
            return;
        }

        RustPlusDeviceProfile device;
        try
        {
            lock (_profileGate)
            {
                if (_profile is null || notification.PlayerId != _profile.SteamId
                    || !_profile.Servers.Any(server => server.ServerId == notification.ServerId))
                {
                    ErrorOccurred?.Invoke("The smart-device pairing does not match a saved server and Steam account; it was ignored.");
                    return;
                }

                var entityName = entity.EntityName;
                device = new RustPlusDeviceProfile
                {
                    ServerId = notification.ServerId,
                    PlayerId = notification.PlayerId,
                    PlayerToken = notification.PlayerToken,
                    EntityId = entity.EntityId.Value,
                    Name = string.IsNullOrWhiteSpace(entity.EntityName)
                        ? $"{entity.EntityType} {entity.EntityId.Value}"
                        : entityName ?? $"{entity.EntityType} {entity.EntityId.Value}",
                    EntityType = entity.EntityType.ToString() ?? "Unknown",
                };
                _profile.Devices.RemoveAll(existing =>
                    existing.ServerId == device.ServerId && existing.EntityId == device.EntityId);
                _profile.Devices.Add(device);
                _store.Save(_profile);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            ErrorOccurred?.Invoke($"Could not securely save the paired smart device: {exception.Message}");
            return;
        }

        EntityPaired?.Invoke(device);
    }

    private RustPlus CreateCompanionClient(Guid serverId, ulong playerId, int playerToken)
    {
        string steamAuthToken;
        lock (_profileGate)
        {
            if (_profile is null || _profile.SteamId != playerId || string.IsNullOrWhiteSpace(_profile.SteamAuthToken))
            {
                throw new InvalidOperationException("Reconnect to Rust+ before accessing paired devices or maps.");
            }

            steamAuthToken = _profile.SteamAuthToken;
        }

        return new RustPlus(
            new CompanionConnection(serverId, steamAuthToken, playerId, playerToken),
            null,
            null);
    }

    private void SaveProfile()
    {
        lock (_profileGate)
        {
            _store.Save(_profile ?? throw new InvalidOperationException("Rust+ profile is not loaded."));
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private string GetListeningStatus() =>
        GetServers().Count == 0
            ? "Steam sign-in complete. Pair a server in Rust+ to see it here; listening for notifications."
            : "Connected to Rust+ notifications. Listening for alarms and pairings.";

    private void DisposeListener()
    {
        if (_listener is null)
        {
            return;
        }

        _listener.Connected -= Listener_Connected;
        _listener.Disconnected -= Listener_Disconnected;
        _listener.ErrorOccurred -= Listener_ErrorOccurred;
        _listener.PersistentIdReceived -= Listener_PersistentIdReceived;
        _listener.OnAlarmTriggered -= Listener_AlarmTriggered;
        _listener.OnServerPairing -= Listener_ServerPairing;
        _listener.OnEntityPairing -= Listener_EntityPairing;
        _listener.Dispose();
        _listener = null;
        _listenerConnected = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
