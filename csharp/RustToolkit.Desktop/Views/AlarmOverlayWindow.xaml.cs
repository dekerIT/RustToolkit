using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RustToolkit.Desktop.Views;

public partial class AlarmOverlayWindow : Window
{
    private readonly Action _acknowledge;
    private readonly DispatcherTimer _flashTimer;
    private bool _dark;

    public AlarmOverlayWindow(string reason, Action acknowledge)
    {
        InitializeComponent();
        ReasonText.Text = reason;
        _acknowledge = acknowledge;
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _flashTimer.Tick += FlashTimer_Tick;
        _flashTimer.Start();
        Closed += (_, _) => _flashTimer.Stop();
    }

    private void FlashTimer_Tick(object? sender, EventArgs e)
    {
        _dark = !_dark;
        Root.Background = new SolidColorBrush(
            _dark
                ? System.Windows.Media.Color.FromRgb(17, 19, 23)
                : System.Windows.Media.Color.FromRgb(165, 31, 37));
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _acknowledge();
}
