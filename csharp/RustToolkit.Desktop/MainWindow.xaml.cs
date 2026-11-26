using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using RustToolkit.Desktop.Services;
using RustToolkit.Desktop.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace RustToolkit.Desktop;

public partial class MainWindow : Window
{
    private const int AlarmHotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF8 = 0x77;
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly AlarmService _alarmService;
    private readonly CountdownTimerService _timerService;
    private readonly WelcomePage _welcomePage;
    private readonly TimersPage _timersPage;
    private readonly CraftingPage _craftingPage;
    private readonly RaidPlannerPage _raidPlannerPage;
    private readonly RustlyApiService _rustlyApiService;
    private readonly RustHelpItemService _rustHelpItemService;
    private readonly RustPlusPage _rustPlusPage;
    private readonly SettingsPage _settingsPage;
    private readonly List<AlarmOverlayWindow> _alarmWindows = [];
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Drawing.Icon _applicationIcon;
    private HwndSource? _windowSource;
    private bool _hotkeyRegistered;
    private bool _exitRequested;
    private readonly RustPlusConnectionService _rustPlusService;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly AppPreferences _preferences;
    private readonly KeepAwakeService _keepAwakeService;
    private bool _keepAwakeEnabled;

    public MainWindow()
    {
        InitializeComponent();

        var stateStore = new AppStateStore();
        _alarmService = new AlarmService();
        _timerService = new CountdownTimerService(stateStore);
        _rustPlusService = new RustPlusConnectionService(new RustPlusProfileStore());
        _preferencesStore = new AppPreferencesStore();
        _preferences = LoadPreferences();
        _keepAwakeService = new KeepAwakeService();
        _keepAwakeEnabled = _preferences.KeepAwakeEnabled;
        _rustlyApiService = new RustlyApiService();
        _rustHelpItemService = new RustHelpItemService();
        _timersPage = new TimersPage(_timerService);
        _craftingPage = new CraftingPage(_rustHelpItemService, _preferencesStore, _preferences);
        _raidPlannerPage = new RaidPlannerPage(_rustlyApiService, _rustHelpItemService);
        _rustPlusPage = new RustPlusPage(_rustPlusService);
        _welcomePage = new WelcomePage();
        _welcomePage.ConnectRequested += WelcomePage_ConnectRequested;
        _welcomePage.ContinueWithoutAccountRequested += WelcomePage_ContinueWithoutAccountRequested;
        _settingsPage = new SettingsPage(IsStartupEnabled(), _keepAwakeEnabled);

        _alarmService.Triggered += AlarmService_Triggered;
        _alarmService.Stopped += AlarmService_Stopped;
        _timerService.NotificationRequested += TimerService_NotificationRequested;
        _timerService.StorageError += TimerService_StorageError;
        _settingsPage.StartupSettingChanged += SettingsPage_StartupSettingChanged;
        _settingsPage.KeepAwakeSettingChanged += SettingsPage_KeepAwakeSettingChanged;
        _rustPlusService.AlarmTriggered += RustPlusService_AlarmTriggered;
        _rustPlusPage.DisconnectCompleted += RustPlusPage_DisconnectCompleted;

        if (_keepAwakeEnabled)
        {
            try
            {
                _keepAwakeService.SetEnabled(true);
            }
            catch (Win32Exception exception)
            {
                _keepAwakeEnabled = false;
                _settingsPage.SetKeepAwakeEnabled(false);
                System.Windows.MessageBox.Show(
                    this,
                    $"Could not keep the PC awake:\n{exception.Message}",
                    "RustToolkit settings",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        _applicationIcon = LoadApplicationIcon();
        _trayIcon = CreateTrayIcon(_applicationIcon);
        PageHost.Content = _preferences.OnboardingCompleted ? _craftingPage : _welcomePage;
        SetNavigationVisible(_preferences.OnboardingCompleted);
        SetNavigationSelection(_preferences.OnboardingCompleted ? _craftingPage : null);
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        var executablePath = Path.Combine(AppContext.BaseDirectory, "RustToolkit.Desktop.exe");
        return Drawing.Icon.ExtractAssociatedIcon(executablePath)
            ?? throw new IOException($"Could not load the application icon from {executablePath}.");
    }

    private static Forms.NotifyIcon CreateTrayIcon(Drawing.Icon applicationIcon)
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = applicationIcon,
            Text = "RustToolkit",
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show RustToolkit", null, (_, _) => ShowWindow());
        menu.Items.Add("Stop Alarm", null, (_, _) => CurrentAlarmService()?.Stop());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => CurrentWindow()?.ExitApplication());
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };
        return icon;
    }

    private static MainWindow? CurrentWindow() =>
        System.Windows.Application.Current?.MainWindow as MainWindow;

    private static AlarmService? CurrentAlarmService() => CurrentWindow()?._alarmService;

    private static void ShowWindow()
    {
        var window = CurrentWindow();
        if (window is null)
        {
            return;
        }

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _hotkeyRegistered = RegisterHotKey(handle, AlarmHotkeyId, ModNoRepeat, VkF8);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == AlarmHotkeyId)
        {
            _alarmService.Stop();
            handled = true;
        }

        return nint.Zero;
    }

    private void ShowTimersPage_Click(object sender, RoutedEventArgs e) => NavigateTo(_timersPage);

    private void ShowCraftingPage_Click(object sender, RoutedEventArgs e) => NavigateTo(_craftingPage);

    private void ShowRustPlusPage_Click(object sender, RoutedEventArgs e) => NavigateTo(_rustPlusPage);

    private void ShowRaidPlannerPage_Click(object sender, RoutedEventArgs e) => NavigateTo(_raidPlannerPage);

    private void ShowSettingsPage_Click(object sender, RoutedEventArgs e) => NavigateTo(_settingsPage);

    private void SetNavigationVisible(bool visible)
    {
        NavigationPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavigationColumn.Width = visible ? new GridLength(224) : new GridLength(0);
    }

    private void NavigateTo(object page)
    {
        SetNavigationSelection(page);
        if (ReferenceEquals(PageHost.Content, page))
        {
            return;
        }

        PageHost.BeginAnimation(UIElement.OpacityProperty, null);
        PageHost.Opacity = 0;
        PageHost.Content = page;

        var duration = TimeSpan.FromMilliseconds(180);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });

        if (PageHost.RenderTransform is not TranslateTransform transform)
        {
            transform = new TranslateTransform();
            PageHost.RenderTransform = transform;
        }

        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = easing });
    }

    private void SetNavigationSelection(object? page)
    {
        TimersNavigationButton.IsChecked = ReferenceEquals(page, _timersPage);
        CraftingNavigationButton.IsChecked = ReferenceEquals(page, _craftingPage);
        RustPlusNavigationButton.IsChecked = ReferenceEquals(page, _rustPlusPage);
        RaidPlannerNavigationButton.IsChecked = ReferenceEquals(page, _raidPlannerPage);
        SettingsNavigationButton.IsChecked = ReferenceEquals(page, _settingsPage);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _rustPlusService.RestoreAsync();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.Net.Http.HttpRequestException
                or System.Net.WebSockets.WebSocketException
                or System.Security.Cryptography.CryptographicException)
        {
            _rustPlusPage.ShowError(exception.Message);
        }
        finally
        {
            _rustPlusPage.RefreshSavedProfile();
        }

        if (_rustPlusService.SteamId is not null)
        {
            CompleteOnboarding();
            if (ReferenceEquals(PageHost.Content, _welcomePage))
            {
                SetNavigationVisible(true);
                NavigateTo(_craftingPage);
            }
        }
    }

    private async void WelcomePage_ConnectRequested(object? sender, EventArgs e)
    {
        CompleteOnboarding();
        SetNavigationVisible(true);
        NavigateTo(_rustPlusPage);
        await _rustPlusPage.BeginConnectionAsync();
    }

    private void WelcomePage_ContinueWithoutAccountRequested(object? sender, EventArgs e)
    {
        CompleteOnboarding();
        SetNavigationVisible(true);
        NavigateTo(_craftingPage);
    }

    private void RustPlusPage_DisconnectCompleted(object? sender, EventArgs e)
    {
        _preferences.OnboardingCompleted = false;
        SavePreferences();
        SetNavigationVisible(false);
        NavigateTo(_welcomePage);
    }

    private void CompleteOnboarding()
    {
        if (_preferences.OnboardingCompleted)
        {
            return;
        }

        _preferences.OnboardingCompleted = true;
        SavePreferences();
    }

    private AppPreferences LoadPreferences()
    {
        try
        {
            return _preferencesStore.Load();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not load application settings:\n{exception.Message}",
                "RustToolkit settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return new AppPreferences();
        }
    }

    private void SavePreferences()
    {
        try
        {
            _preferencesStore.Save(_preferences);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not save application settings:\n{exception.Message}",
                "RustToolkit settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RustPlusService_AlarmTriggered(string reason) =>
        Dispatcher.BeginInvoke(() => _alarmService.Trigger(reason));

    private void AlarmService_Triggered(string reason)
    {
        CloseAlarmWindows();

        foreach (var screen in Forms.Screen.AllScreens)
        {
            var bounds = screen.Bounds;
            var window = new AlarmOverlayWindow(reason, _alarmService.Stop)
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
            };
            window.Show();
            _alarmWindows.Add(window);
        }
    }

    private void AlarmService_Stopped()
    {
        CloseAlarmWindows();
    }

    private void TimerService_NotificationRequested(string message)
    {
        _trayIcon.BalloonTipTitle = "RustToolkit timer";
        _trayIcon.BalloonTipText = message;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void TimerService_StorageError(string message)
    {
        System.Windows.MessageBox.Show(
            this,
            message,
            "RustToolkit timers",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void SettingsPage_StartupSettingChanged(bool enabled)
    {
        try
        {
            UpdateStartupRegistration(enabled);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _settingsPage.SetStartupEnabled(IsStartupEnabled());
            System.Windows.MessageBox.Show(
                this,
                $"Could not update the Windows startup setting:\n{exception.Message}",
                "RustToolkit settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SettingsPage_KeepAwakeSettingChanged(bool enabled)
    {
        try
        {
            _keepAwakeService.SetEnabled(enabled);
            _preferences.KeepAwakeEnabled = enabled;
            _preferencesStore.Save(_preferences);
            _keepAwakeEnabled = enabled;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            string? rollbackError = null;
            try
            {
                _keepAwakeService.SetEnabled(_keepAwakeEnabled);
            }
            catch (Win32Exception rollbackException)
            {
                rollbackError = rollbackException.Message;
            }

            _preferences.KeepAwakeEnabled = _keepAwakeEnabled;
            _settingsPage.SetKeepAwakeEnabled(_keepAwakeEnabled);
            var message = $"Could not update the keep-awake setting:\n{exception.Message}";
            if (rollbackError is not null)
            {
                message += $"\n\nCould not restore the previous system state:\n{rollbackError}";
            }

            System.Windows.MessageBox.Show(
                this,
                message,
                "RustToolkit settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue("RustToolkit") is string;
    }

    private static void UpdateStartupRegistration(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new IOException("Could not open the Windows startup registry key.");

        if (enabled)
        {
            var processPath = Environment.ProcessPath
                ?? throw new IOException("Could not determine the application path.");
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            var isDotnetHost = string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase);
            var command = isDotnetHost && !string.IsNullOrWhiteSpace(entryAssembly)
                ? $"\"{processPath}\" \"{entryAssembly}\""
                : $"\"{processPath}\"";
            key.SetValue("RustToolkit", command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("RustToolkit", throwOnMissingValue: false);
        }
    }

    private void CloseAlarmWindows()
    {
        foreach (var window in _alarmWindows)
        {
            window.Close();
        }

        _alarmWindows.Clear();
    }

    private void ExitApplication()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _alarmService.Stop();
        _timerService.Dispose();
        _rustPlusService.Dispose();
        _rustlyApiService.Dispose();
        _rustHelpItemService.Dispose();
        try
        {
            _keepAwakeService.Dispose();
        }
        catch (Win32Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                $"Could not restore the Windows sleep setting:\n{exception.Message}",
                "RustToolkit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        CloseAlarmWindows();
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(new WindowInteropHelper(this).Handle, AlarmHotkeyId);
            _hotkeyRegistered = false;
        }

        _windowSource?.RemoveHook(WindowMessageHook);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _applicationIcon.Dispose();
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.ShowBalloonTip(
                3000,
                "RustToolkit",
                "The app is still running in the system tray.",
                Forms.ToolTipIcon.Info);
            return;
        }

        _alarmService.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
