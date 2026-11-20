using System.Text.Json;
using System.IO;
using RustToolkit.Desktop.Models;

namespace RustToolkit.Desktop.Services;

public sealed class AppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustToolkit",
        "timers.json");

    public List<TimerEntry> LoadTimers()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<TimerEntry>>(
                File.ReadAllText(_path),
                JsonOptions) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Timer data is invalid. The file was not changed: {_path}",
                exception);
        }
    }

    public void SaveTimers(IEnumerable<TimerEntry> timers)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new IOException("Could not determine the timer data directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(timers, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
