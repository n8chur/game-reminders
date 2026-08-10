using System.Text.Json;
using GameReminders.Core;

namespace GameReminders.App;

public sealed record AppSettings
{
    public string? ICloudRoot { get; init; }
}

public sealed class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameReminders");
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    internal SettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonProtocol.Options)
                ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            ReminderStore.AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonProtocol.Options));
        }
        catch (IOException)
        {
            // Settings are a convenience cache. A transient local write failure
            // must not prevent the iCloud-backed reminder app from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // Continue with the settings already loaded for this session.
        }
    }
}
