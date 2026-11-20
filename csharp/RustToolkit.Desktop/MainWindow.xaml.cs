using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using RustToolkit.Desktop.Services;
using RustToolkit.Desktop.Views;
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
    private readonly AlarmPage _alarmPage;
    private readonly TimersPage _timersPage;
    private readonly RustPlusPage _rustPlusPage;
    private readonly SettingsPage _settingsPage;
    private readonly List<AlarmOverlayWindow> _alarmWindows = [];
    private readonly Forms.NotifyIcon _trayIcon;
    private HwndSource? _windowSource;
    private bool _hotkeyRegistered;
    private bool _exitRequested;
    private readonly RustPlusConnectionService _rustPlusService;

    public MainWindow()
    {
        InitializeComponent();

        var stateStore = new AppStateStore();
        _alarmService = new AlarmService();
        _timerService = new CountdownTimerService(stateStore);
        _rustPlusService = new RustPlusConnectionService(new RustPlusProfileStore());
        _alarmPage = new AlarmPage(_alarmService);
        _timersPage = new TimersPage(_timerService);
        _rustPlusPage = new RustPlusPage(_rustPlusService);
        _settingsPage = new SettingsPage(IsStartupEnabled());

        _alarmService.Triggered += AlarmService_Triggered;
        _alarmService.Stopped += AlarmService_Stopped;
        _timerService.NotificationRequested += TimerService_NotificationRequested;
        _timerService.StorageError += TimerService_StorageError;
        _settingsPage.StartupSettingChanged += SettingsPage_StartupSettingChanged;
        _rustPlusService.AlarmTriggered += RustPlusService_AlarmTriggered;

        _trayIcon = CreateTrayIcon();
        PageHost.Content = _alarmPage;
    }

    private static Forms.NotifyIcon CreateTrayIcon()
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "RustToolkit",
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show RustToolkit", null, (_, _) => ShowWindow());
        menu.Items.Add("Test Alarm", null, (_, _) => CurrentAlarmService()?.Trigger("Tray test"));
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
        _alarmPage.SetHotkeyStatus(_hotkeyRegistered);
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

    private void ShowAlarmPage_Click(object sender, RoutedEventArgs e) => PageHost.Content = _alarmPage;

    private void ShowTimersPage_Click(object sender, RoutedEventArgs e) => PageHost.Content = _timersPage;

    private void ShowRustPlusPage_Click(object sender, RoutedEventArgs e) => PageHost.Content = _rustPlusPage;

    private void ShowSettingsPage_Click(object sender, RoutedEventArgs e) => PageHost.Content = _settingsPage;

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
    }

    private void RustPlusService_AlarmTriggered(string reason) =>
        Dispatcher.BeginInvoke(() => _alarmService.Trigger(reason));

    private void AlarmService_Triggered(string reason)
    {
        _alarmPage.SetAlarmStatus(true, reason);
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
        _alarmPage.SetAlarmStatus(false, string.Empty);
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
        CloseAlarmWindows();
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(new WindowInteropHelper(this).Handle, AlarmHotkeyId);
            _hotkeyRegistered = false;
        }

        _windowSource?.RemoveHook(WindowMessageHook);
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
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
