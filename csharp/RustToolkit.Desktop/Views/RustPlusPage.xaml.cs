using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RustPlusApi.Data.Markers;
using RustToolkit.Desktop.Models;
using RustToolkit.Desktop.Services;
using Brushes = System.Windows.Media.Brushes;
using WpfImage = System.Windows.Controls.Image;

namespace RustToolkit.Desktop.Views;

public partial class RustPlusPage : System.Windows.Controls.UserControl
{
    private readonly RustPlusConnectionService _service;
    private bool? _selectedDeviceActive;

    public RustPlusPage(RustPlusConnectionService service)
    {
        InitializeComponent();
        _service = service;
        StatusText.Text = service.Status;
        RefreshSavedProfile();
        service.StatusChanged += Service_StatusChanged;
        service.ErrorOccurred += Service_ErrorOccurred;
        service.ServerPaired += Service_ServerPaired;
        service.EntityPaired += Service_EntityPaired;
    }

    public event EventHandler? DisconnectCompleted;

    private async void Connect_Click(object sender, RoutedEventArgs e) =>
        await RunConnectionAsync(restore: false);

    private async void Reconnect_Click(object sender, RoutedEventArgs e) =>
        await RunConnectionAsync(restore: true);

    public Task BeginConnectionAsync() => RunConnectionAsync(restore: false);

    private async Task RunConnectionAsync(bool restore)
    {
        ConnectButton.IsEnabled = false;
        ReconnectButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            if (restore)
            {
                await _service.RestoreAsync();
            }
            else
            {
                await _service.ConnectAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ShowError("The Rust+ connection attempt was cancelled.");
        }
        catch (Exception exception)
        {
            ShowError(FormatException(exception));
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            ReconnectButton.IsEnabled = _service.SteamId is not null;
            RefreshSavedProfile();
        }
    }

    public void RefreshSavedProfile(Guid? preferredServerId = null)
    {
        UpdateAuthenticationButtons();

        var selectedServerId = preferredServerId
            ?? (ServerList.SelectedItem as RustPlusServerProfile)?.ServerId;
        var servers = _service.GetServers();
        ServerList.ItemsSource = servers;
        ServerList.SelectedItem = servers.FirstOrDefault(server => server.ServerId == selectedServerId)
            ?? servers.FirstOrDefault();
        UpdateSelectedServer();
    }

    private void UpdateAuthenticationButtons()
    {
        var steamId = _service.SteamId;
        var isConnected = steamId is not null;
        SteamAccountText.Text = steamId is ulong accountId
            ? $"Steam account connected · SteamID {accountId}"
            : "Steam account not connected.";
        ConnectButton.Visibility = isConnected ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
        DisconnectButton.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "Sign out and remove this Rust+ account, paired servers, and saved device tokens from this PC?",
            "Disconnect Rust+",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        DisconnectButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Text = "Disconnecting from Rust+…";
        try
        {
            await _service.DisconnectAsync();
            RefreshSavedProfile();
            DisconnectCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError($"Could not disconnect from Rust+: {exception.Message}");
            RefreshSavedProfile();
        }
        finally
        {
            DisconnectButton.IsEnabled = _service.SteamId is not null;
        }
    }

    private void ServerList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedServer();

    private void UpdateSelectedServer()
    {
        var server = ServerList.SelectedItem as RustPlusServerProfile;
        LoadMapButton.IsEnabled = server is not null;
        DeviceList.ItemsSource = server is null ? [] : _service.GetDevices(server.ServerId);
        DeviceStatusText.Text = "Select a paired device to read its state.";
        _selectedDeviceActive = null;
        RefreshDeviceButton.IsEnabled = false;
        ToggleDeviceButton.IsEnabled = false;
        MapSurface.Children.Clear();
        MapStatusText.Text = server is null
            ? "Select a paired server to load its map."
            : $"Select Load Map to view {server.Name}.";
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDeviceActive = null;
        RefreshDeviceButton.IsEnabled = DeviceList.SelectedItem is RustPlusDeviceProfile;
        ToggleDeviceButton.IsEnabled = false;
        DeviceStatusText.Text = DeviceList.SelectedItem is RustPlusDeviceProfile
            ? "Select Read State to query this device."
            : "No device selected.";
    }

    private async void LoadMap_Click(object sender, RoutedEventArgs e)
    {
        if (ServerList.SelectedItem is not RustPlusServerProfile server)
        {
            return;
        }

        LoadMapButton.IsEnabled = false;
        MapStatusText.Text = $"Loading map for {server.Name}…";
        try
        {
            var snapshot = await _service.LoadMapAsync(server);
            DisplayMap(snapshot, server.Name);
        }
        catch (OperationCanceledException)
        {
            MapStatusText.Text = "Map loading was cancelled.";
        }
        catch (Exception exception)
        {
            MapStatusText.Text = $"Could not load map: {FormatException(exception)}";
        }
        finally
        {
            LoadMapButton.IsEnabled = ServerList.SelectedItem is RustPlusServerProfile;
        }
    }

    private async void RefreshDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not RustPlusDeviceProfile device)
        {
            return;
        }

        RefreshDeviceButton.IsEnabled = false;
        ToggleDeviceButton.IsEnabled = false;
        DeviceStatusText.Text = "Reading device state…";
        try
        {
            var state = await _service.LoadDeviceStateAsync(device);
            ShowDeviceState(device, state);
        }
        catch (OperationCanceledException)
        {
            DeviceStatusText.Text = "Reading the device state was cancelled.";
        }
        catch (Exception exception)
        {
            DeviceStatusText.Text = $"Could not read device state: {FormatException(exception)}";
        }
        finally
        {
            RefreshDeviceButton.IsEnabled = DeviceList.SelectedItem is RustPlusDeviceProfile;
        }
    }

    private async void ToggleDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not RustPlusDeviceProfile device || _selectedDeviceActive is null)
        {
            return;
        }

        RefreshDeviceButton.IsEnabled = false;
        ToggleDeviceButton.IsEnabled = false;
        var requestedValue = !_selectedDeviceActive.Value;
        DeviceStatusText.Text = requestedValue ? "Turning switch on…" : "Turning switch off…";
        try
        {
            var state = await _service.SetSmartSwitchValueAsync(device, requestedValue);
            ShowDeviceState(device, state);
        }
        catch (OperationCanceledException)
        {
            DeviceStatusText.Text = "Updating the Smart Switch was cancelled.";
        }
        catch (Exception exception)
        {
            DeviceStatusText.Text = $"Could not control the Smart Switch: {FormatException(exception)}";
        }
        finally
        {
            RefreshDeviceButton.IsEnabled = DeviceList.SelectedItem is RustPlusDeviceProfile;
        }
    }

    private void ShowDeviceState(RustPlusDeviceProfile device, RustPlusDeviceState state)
    {
        _selectedDeviceActive = state.IsActive;
        DeviceStatusText.Text = $"{device.Name}: {state.Description}";
        ToggleDeviceButton.Content = state.IsActive == true ? "Turn Off" : "Turn On";
        ToggleDeviceButton.IsEnabled = state.IsActive is not null
            && string.Equals(device.EntityType, "Switch", StringComparison.OrdinalIgnoreCase);
    }

    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string FormatException(Exception exception)
    {
        var message = $"{exception.GetType().Name}: {exception.Message}";
        if (exception.InnerException is { } inner)
        {
            message += $"\n\nCaused by: {inner.GetType().Name}: {inner.Message}";
        }

        return message;
    }

    private void DisplayMap(RustPlusMapSnapshot snapshot, string serverName)
    {
        var map = snapshot.Map;
        if (map.Width is not > 0 || map.Height is not > 0 || map.OceanMargin is null || snapshot.MapSize <= 0)
        {
            throw new InvalidOperationException("The server returned invalid map dimensions.");
        }

        var width = (double)map.Width.Value;
        var height = (double)map.Height.Value;
        MapSurface.Width = width;
        MapSurface.Height = height;
        MapSurface.Children.Clear();
        var image = new BitmapImage();
        using (var stream = new MemoryStream(map.JpgImage!))
        {
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
        }

        MapSurface.Children.Add(new WpfImage
        {
            Source = image,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
        });

        if (map.Monuments is not null)
        {
            foreach (var monument in map.Monuments)
            {
                AddMapMarker(monument.X, monument.Y, monument.Name ?? "Monument", Brushes.White, snapshot);
            }
        }

        AddMarkers(snapshot.Markers.PlayerMarkers.Values, marker => marker.Name, "Player", Brushes.LimeGreen, snapshot);
        AddMarkers(snapshot.Markers.CargoShipMarkers.Values, _ => "Cargo ship", "Cargo ship", Brushes.Orange, snapshot);
        AddMarkers(snapshot.Markers.Ch47Markers.Values, _ => "Chinook", "Chinook", Brushes.Orange, snapshot);
        AddMarkers(snapshot.Markers.CrateMarkers.Values, _ => "Locked crate", "Locked crate", Brushes.Gold, snapshot);
        AddMarkers(snapshot.Markers.ExplosionMarkers.Values, _ => "Explosion", "Explosion", Brushes.Red, snapshot);
        AddMarkers(snapshot.Markers.PatrolHelicopterMarkers.Values, _ => "Patrol helicopter", "Patrol helicopter", Brushes.OrangeRed, snapshot);
        AddMarkers(snapshot.Markers.TravellingVendorMarkers.Values, _ => "Travelling vendor", "Travelling vendor", Brushes.DeepSkyBlue, snapshot);
        AddMarkers(snapshot.Markers.VendingMachineMarkers.Values, marker => marker.Name, "Vending machine", Brushes.DeepSkyBlue, snapshot);
        AddMarkers(snapshot.Markers.GenericRadiusMarkers.Values, _ => "Map event", "Map event", Brushes.Magenta, snapshot);
        AddMarkers(snapshot.Markers.UnknownMarkers.Values, marker => marker.Name, "Unknown marker", Brushes.Gray, snapshot);

        var playerCount = snapshot.Markers.PlayerMarkers.Count;
        var eventCount = snapshot.Markers.CargoShipMarkers.Count
            + snapshot.Markers.Ch47Markers.Count
            + snapshot.Markers.CrateMarkers.Count
            + snapshot.Markers.ExplosionMarkers.Count
            + snapshot.Markers.PatrolHelicopterMarkers.Count
            + snapshot.Markers.TravellingVendorMarkers.Count;
        MapStatusText.Text = $"{serverName} · {map.Monuments?.Count ?? 0} monuments · {playerCount} players · {eventCount} events";
    }

    private void AddMarkers<TMarker>(
        IEnumerable<TMarker> markers,
        Func<TMarker, string?> nameSelector,
        string fallbackName,
        System.Windows.Media.Brush brush,
        RustPlusMapSnapshot snapshot)
        where TMarker : Marker
    {
        foreach (var marker in markers)
        {
            var name = nameSelector(marker);
            AddMapMarker(marker.X, marker.Y, string.IsNullOrWhiteSpace(name) ? fallbackName : name, brush, snapshot);
        }
    }

    private void AddMapMarker(
        double? worldX,
        double? worldY,
        string label,
        System.Windows.Media.Brush brush,
        RustPlusMapSnapshot snapshot)
    {
        var map = snapshot.Map;
        if (worldX is null || worldY is null || map.Width is null || map.Height is null || map.OceanMargin is null)
        {
            return;
        }

        var width = (double)map.Width.Value;
        var height = (double)map.Height.Value;
        var oceanMargin = (double)map.OceanMargin.Value;
        var drawableWidth = width - (2 * oceanMargin);
        var drawableHeight = height - (2 * oceanMargin);
        if (drawableWidth <= 0 || drawableHeight <= 0)
        {
            return;
        }

        var x = worldX.Value * (drawableWidth / snapshot.MapSize) + oceanMargin;
        var y = height - (worldY.Value * (drawableHeight / snapshot.MapSize) + oceanMargin);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x > width || y > height)
        {
            return;
        }

        var marker = new Border
        {
            Width = 10,
            Height = 10,
            Background = brush,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ToolTip = label,
        };
        Canvas.SetLeft(marker, x - 5);
        Canvas.SetTop(marker, y - 5);
        MapSurface.Children.Add(marker);
    }

    private void Service_StatusChanged(string status) =>
        Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = status;
            UpdateAuthenticationButtons();
        });

    private void Service_ErrorOccurred(string message) =>
        Dispatcher.BeginInvoke(() => ShowError(message));

    private void Service_ServerPaired(RustPlusServerProfile server) =>
        Dispatcher.BeginInvoke(() =>
        {
            RefreshSavedProfile(server.ServerId);
        });

    private void Service_EntityPaired(RustPlusDeviceProfile device) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (ServerList.SelectedItem is RustPlusServerProfile server && server.ServerId == device.ServerId)
            {
                DeviceList.ItemsSource = _service.GetDevices(server.ServerId);
                DeviceList.SelectedItem = device;
            }

            StatusText.Text = $"Paired smart device: {device.Name}";
        });
}
