using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace RustToolkit.Desktop.Services;

public static class NotesApplicationLauncher
{
    private const string NotesFolderName = "notes gui";
    private const string NotesExecutableName = "notes with notepad++.exe";

    public static void Start()
    {
        var notesDirectory = Path.Combine(AppContext.BaseDirectory, NotesFolderName);
        var executablePath = Path.Combine(notesDirectory, NotesExecutableName);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The notes application was not found at {executablePath}.",
                executablePath);
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = notesDirectory,
            UseShellExecute = true,
        });

        if (process is null)
        {
            throw new InvalidOperationException("Windows did not start the notes application.");
        }

        process.Dispose();
    }
}
