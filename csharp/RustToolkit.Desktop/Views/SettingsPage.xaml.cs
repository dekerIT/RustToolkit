using System.Windows;
using System.Windows.Controls;

namespace RustToolkit.Desktop.Views;

public partial class SettingsPage : System.Windows.Controls.UserControl
{
    private bool _initialized;

    public SettingsPage(bool startWithWindows)
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = startWithWindows;
        _initialized = true;
    }

    public event Action<bool>? StartupSettingChanged;

    public void SetStartupEnabled(bool enabled)
    {
        _initialized = false;
        StartupCheckBox.IsChecked = enabled;
        _initialized = true;
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            StartupSettingChanged?.Invoke(StartupCheckBox.IsChecked == true);
        }
    }
}
