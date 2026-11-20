using System.IO;
using System.ComponentModel;
using System.Windows;
using RustToolkit.Desktop.Services;

namespace RustToolkit.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            NotesApplicationLauncher.Start();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Security.SecurityException
                or Win32Exception
                or InvalidOperationException)
        {
            System.Windows.MessageBox.Show(
                $"RustToolkit could not start the notes application:\n{exception.Message}",
                "Notes application startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            System.Windows.MessageBox.Show(
                exception.Message,
                "RustToolkit could not start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
