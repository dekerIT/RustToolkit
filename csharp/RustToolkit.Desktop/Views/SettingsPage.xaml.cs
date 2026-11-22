using System.Windows;
using System.Windows.Controls;

namespace RustToolkit.Desktop.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _initialized;

    public SettingsPage(bool startWithWindows, bool keepAwakeEnabled)
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = startWithWindows;
        KeepAwakeCheckBox.IsChecked = keepAwakeEnabled;
        _initialized = true;
    }

    public event Action<bool>? StartupSettingChanged;
    public event Action<bool>? KeepAwakeSettingChanged;

    public void SetStartupEnabled(bool enabled)
    {
        _initialized = false;
        StartupCheckBox.IsChecked = enabled;
        _initialized = true;
    }

    public void SetKeepAwakeEnabled(bool enabled)
    {
        _initialized = false;
        KeepAwakeCheckBox.IsChecked = enabled;
        _initialized = true;
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            StartupSettingChanged?.Invoke(StartupCheckBox.IsChecked == true);
        }
    }

    private void KeepAwakeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            KeepAwakeSettingChanged?.Invoke(KeepAwakeCheckBox.IsChecked == true);
        }
    }
}
