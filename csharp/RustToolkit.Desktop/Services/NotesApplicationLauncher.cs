using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace RustToolkit.Desktop.Services;

public static class NotesApplicationLauncher
{
    private const string NotesFolderName = "notes";
    private const string NotesExecutableName = "notes.exe";

    public static void Start()
    {
        var executableDirectory = Path.Combine(AppContext.BaseDirectory, NotesFolderName);
        var executablePath = Path.Combine(executableDirectory, NotesExecutableName);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The notes application was not found at {executablePath}.",
                executablePath);
        }

        var workingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RustToolkit",
            "Notes");
        Directory.CreateDirectory(workingDirectory);

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not start the notes application.");
        }

        process.Dispose();
    }
}
