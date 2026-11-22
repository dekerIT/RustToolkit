using System.IO;
using System.Text.Json;

namespace RustToolkit.Desktop.Services;

public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RustToolkit",
        "settings.json");

    public AppPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return new AppPreferences();
        }

        try
        {
            var preferences = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(_path), JsonOptions)
                ?? new AppPreferences();
            preferences.FavoriteCraftingShortNames = (preferences.FavoriteCraftingShortNames ?? [])
                .Where(shortName => !string.IsNullOrWhiteSpace(shortName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return preferences;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Application settings are invalid. The file was not changed: {_path}",
                exception);
        }
    }

    public void Save(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new IOException("Could not determine the application settings directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{_path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}

public sealed class AppPreferences
{
    public bool KeepAwakeEnabled { get; set; }
    public bool OnboardingCompleted { get; set; }
    public List<string> FavoriteCraftingShortNames { get; set; } = [];
}
