using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RustToolkit.Desktop.Views;

public partial class WelcomePage : System.Windows.Controls.UserControl
{
    private bool _hasAnimated;

    public WelcomePage()
    {
        InitializeComponent();
    }

    public event EventHandler? ConnectRequested;
    public event EventHandler? ContinueWithoutAccountRequested;

    private void WelcomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasAnimated)
        {
            return;
        }

        _hasAnimated = true;
        var duration = TimeSpan.FromMilliseconds(440);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        WelcomeContent.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        if (WelcomeContent.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, duration) { EasingFunction = easing });
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e) =>
        ConnectRequested?.Invoke(this, EventArgs.Empty);

    private void Continue_Click(object sender, RoutedEventArgs e) =>
        ContinueWithoutAccountRequested?.Invoke(this, EventArgs.Empty);
}
