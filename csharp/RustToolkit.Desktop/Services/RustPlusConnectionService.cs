using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
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
        listener.NotificationReceived += Listener_NotificationReceived;
        listener.OnAlarmTriggered += Listener_AlarmTriggered;
        listener.OnPairing += Listener_OnPairing;
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

    private void Listener_OnPairing(object? sender, FcmMessage message)
    {
        try
        {
            var data = message.Data;
            var body = data?.Body;
            if (body is null)
            {
                return;
            }

            var isServer = string.Equals(body.Type, "server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(data?.ChannelId, "server", StringComparison.OrdinalIgnoreCase);
            if (isServer)
            {
                HandleServerPairingBody(body);
            }
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                $"Rust+ pairing handler failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void HandleServerPairingBody(Body body)
    {
        var ip = body.Ip?.Trim();
        if (string.IsNullOrWhiteSpace(ip) || body.Port is < 1 or > 65535)
        {
            return;
        }

        if (!int.TryParse(body.PlayerToken, out var playerToken))
        {
            ErrorOccurred?.Invoke(
                $"Rust+ sent an invalid player token '{body.PlayerToken}' for {ip}:{body.Port}; the server was not saved.");
            return;
        }

        var serverId = body.Id;
        if (serverId == Guid.Empty)
        {
            ErrorOccurred?.Invoke(
                $"Rust+ sent an invalid server id '{body.Id}' for {ip}:{body.Port}; the server was not saved.");
            return;
        }

        RustPlusServerProfile pairedServer;
        try
        {
            lock (_profileGate)
            {
                if (_profile is null || body.PlayerId != _profile.SteamId)
                {
                    ErrorOccurred?.Invoke(
                        $"The server pairing belongs to Steam account {body.PlayerId}, but this app is signed in as {(_profile?.SteamId ?? 0)}; it was ignored.");
                    return;
                }

                pairedServer = new RustPlusServerProfile
                {
                    ServerId = serverId,
                    Name = string.IsNullOrWhiteSpace(body.Name) ? ip : body.Name!,
                    Ip = ip,
                    Port = body.Port,
                    PlayerId = body.PlayerId,
                    PlayerToken = playerToken,
                };
                _profile.Servers.RemoveAll(existing =>
                    existing.ServerId != Guid.Empty && existing.ServerId == pairedServer.ServerId
                    || existing.Ip == pairedServer.Ip && existing.Port == pairedServer.Port);
                _profile.Servers.Add(pairedServer);
                _store.Save(_profile);
            }
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                $"Could not save the paired server: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        ServerPaired?.Invoke(pairedServer);
        SetStatus($"Paired with {pairedServer.Name}. Rust+ notifications are active.");
    }

    private void Listener_NotificationReceived(object? sender, string json)
    {
        try
        {
            TryWriteDiagnostic(json);
            if (TryParseServerPairing(json, out var profile, out var failure))
            {
                SaveServerPairing(profile);
            }
            else if (failure is not null)
            {
                ErrorOccurred?.Invoke(failure);
            }
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                $"Rust+ raw notification handler failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool TryParseServerPairing(string json, out RustPlusServerProfile profile, out string? failure)
    {
        profile = null!;
        failure = null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var data = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "data", StringComparison.OrdinalIgnoreCase))
                {
                    data = property.Value;
                    break;
                }
            }
        }

        if (data.ValueKind == JsonValueKind.String)
        {
            var inner = data.GetString();
            if (string.IsNullOrWhiteSpace(inner))
            {
                return false;
            }

            using var innerDocument = JsonDocument.Parse(inner);
            data = innerDocument.RootElement;
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var channel = FindString(data, "ch", "channelId", "channel");
        var type = FindString(data, "type");
        var isServer = string.Equals(channel, "server", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "server", StringComparison.OrdinalIgnoreCase);
        if (!isServer)
        {
            return false;
        }

        var ip = FindString(data, "ip", "addr", "address", "serverIp", "server_ip") ?? string.Empty;
        var portText = FindString(data, "port", "serverPort", "server_port");
        var name = FindString(data, "name", "serverName", "server_name", "title") ?? string.Empty;
        var serverIdText = FindString(data, "serverId", "server_id", "id");
        var playerIdText = FindString(data, "playerId", "player_id");
        var playerTokenText = FindString(data, "playerToken", "player_token", "token");

        if (!Guid.TryParse(serverIdText, out var serverId)
            || serverId == Guid.Empty
            || !ulong.TryParse(playerIdText, out var playerId)
            || !int.TryParse(playerTokenText, out var playerToken))
        {
            failure =
                $"Rust+ sent incomplete server-pairing data (serverId='{serverIdText}', playerId='{playerIdText}', token='{playerTokenText}', steamId={SteamId}). Raw: {Truncate(json, 600)}";
            Diag($"PARSE_SERVER_FAIL {failure}");
            return false;
        }

        if (playerId != SteamId)
        {
            failure =
                $"The server pairing belongs to Steam account {playerId}, but this app is signed in as {SteamId}; it was ignored.";
            return false;
        }

        var port = int.TryParse(portText, out var parsedPort) && parsedPort is >= 1 and <= 65535
            ? parsedPort
            : 0;

        profile = new RustPlusServerProfile
        {
            ServerId = serverId,
            Name = string.IsNullOrWhiteSpace(name) ? serverId.ToString() : name,
            Ip = ip,
            Port = port,
            PlayerId = playerId,
            PlayerToken = playerToken,
        };
        Diag($"PARSE_SERVER_OK {serverId} {profile.Name}");
        return true;
    }

    private static string? FindString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "body", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Object)
            {
                return FindString(property.Value, names);
            }
        }

        return null;
    }

    private void SaveServerPairing(RustPlusServerProfile pairedServer)
    {
        try
        {
            lock (_profileGate)
            {
                if (_profile is null)
                {
                    return;
                }

                _profile.Servers.RemoveAll(existing => existing.ServerId == pairedServer.ServerId);
                _profile.Servers.Add(pairedServer);
                _store.Save(_profile);
                Diag($"SAVE_SERVER {pairedServer.ServerId} {pairedServer.Name} count={_profile.Servers.Count}");
            }
        }
        catch (Exception exception)
        {
            Diag($"SAVE_SERVER_ERR {exception.GetType().Name}: {exception.Message}");
            ErrorOccurred?.Invoke(
                $"Could not save the paired server: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        ServerPaired?.Invoke(pairedServer);
        SetStatus($"Paired with {pairedServer.Name}. Rust+ notifications are active.");
    }

    private static string Truncate(string value, int maximum)
    {
        value ??= string.Empty;
        return value.Length <= maximum ? value : value[..maximum] + "…";
    }

    private static void TryWriteDiagnostic(string json)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustToolkit",
                "last-notifications.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, json + Environment.NewLine + "----" + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void Diag(string message)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RustToolkit",
                "last-notifications.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, "DIAG " + message + Environment.NewLine + "----" + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void Listener_ServerPairing(object? sender, Notification<ServerEvent?> notification)
    {
        var server = notification.Data;
        if (server is null || string.IsNullOrWhiteSpace(server.Ip) || server.Port is < 1 or > 65535)
        {
            return;
        }

        RustPlusServerProfile pairedServer;
        try
        {
            lock (_profileGate)
            {
                if (_profile is null || notification.PlayerId != _profile.SteamId)
                {
                    ErrorOccurred?.Invoke(
                        $"The server pairing belongs to Steam account {notification.PlayerId}, but this app is signed in as {(_profile?.SteamId ?? 0)}; it was ignored.");
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
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                $"Could not save the paired server: {exception.GetType().Name}: {exception.Message}");
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
            ErrorOccurred?.Invoke(
                $"Rust+ sent incomplete smart-device pairing data (entityId={entity?.EntityId}, serverId={notification.ServerId}, type={entity?.EntityType}); the device was not saved.");
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
                    Diag(
                        $"ENTITY_REJECT serverId={notification.ServerId} playerId={notification.PlayerId} steamId={_profile?.SteamId ?? 0} servers={_profile?.Servers.Count ?? 0}");
                    ErrorOccurred?.Invoke(
                        $"The smart-device pairing (playerId={notification.PlayerId}, serverId={notification.ServerId}) does not match the signed-in account (steamId={_profile?.SteamId ?? 0}) or a saved server; it was ignored.");
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
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(
                $"Could not save the paired smart device: {exception.GetType().Name}: {exception.Message}");
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
        _listener.NotificationReceived -= Listener_NotificationReceived;
        _listener.OnAlarmTriggered -= Listener_AlarmTriggered;
        _listener.OnPairing -= Listener_OnPairing;
        _listener.OnServerPairing -= Listener_ServerPairing;
        _listener.OnEntityPairing -= Listener_EntityPairing;
        _listener.Dispose();
        _listener = null;
        _listenerConnected = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
