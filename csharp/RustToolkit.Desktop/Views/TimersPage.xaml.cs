using System.Windows;
using System.Windows.Controls;
using System.IO;
using RustToolkit.Desktop.Services;

namespace RustToolkit.Desktop.Views;

public partial class TimersPage : System.Windows.Controls.UserControl
{
    private readonly CountdownTimerService _timerService;

    public TimersPage(CountdownTimerService timerService)
    {
        InitializeComponent();
        _timerService = timerService;
        TimerList.ItemsSource = timerService.Timers;
    }

    private void AddTimer_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!int.TryParse(MinutesBox.Text, out var minutes))
        {
            ShowError("Duration must be a whole number of minutes.");
            return;
        }

        try
        {
            _timerService.Add(TimerNameBox.Text, minutes);
            TimerNameBox.Clear();
        }
        catch (ArgumentException exception)
        {
            ShowError(exception.Message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save the timer: {exception.Message}");
        }
    }

    private void RemoveTimer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: Guid id })
        {
            try
            {
                _timerService.Remove(id);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                System.Windows.MessageBox.Show(
                    $"Could not save timer changes:\n{exception.Message}",
                    "RustToolkit timers",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
