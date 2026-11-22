using System.Windows;
using System.Windows.Controls;
using RustToolkit.Desktop.Services;

namespace RustToolkit.Desktop.Views;

public partial class AlarmPage : System.Windows.Controls.UserControl
{
    private readonly AlarmService _alarmService;

    public AlarmPage(AlarmService alarmService)
    {
        InitializeComponent();
        _alarmService = alarmService;
    }

    public void SetAlarmStatus(bool active, string reason)
    {
        StatusText.Text = active ? "ALARM ACTIVE" : "READY";
        StatusText.Foreground = active
            ? System.Windows.Media.Brushes.OrangeRed
            : System.Windows.Media.Brushes.White;
        ReasonText.Text = active ? reason : "No active alert";
    }

    public void SetHotkeyStatus(bool registered)
    {
        HotkeyText.Text = registered
            ? "F8 — stop the active alarm"
            : "F8 is unavailable; stop the alarm using the app or tray.";
    }

    private void TriggerAlarm_Click(object sender, RoutedEventArgs e) =>
        _alarmService.Trigger("TEST: Manual siren check");

    private void StopAlarm_Click(object sender, RoutedEventArgs e) => _alarmService.Stop();
}
