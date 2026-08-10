using System.Text.Json;
using GameReminders.Core;

namespace GameReminders.App;

public sealed record AppSettings
{
    public string? ICloudRoot { get; init; }
    public IReadOnlyList<PendingGameDetection> PendingDetections { get; init; } = [];
    public IReadOnlyList<string> IgnoredDetectionKeys { get; init; } = [];
}

public sealed record PendingGameDetection
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Processes { get; init; } = [];
    public required string SourceType { get; init; }
    public string? AppId { get; init; }
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
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
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonProtocol.Options)
                ?? new AppSettings();
            var pending = (settings.PendingDetections ?? [])
                .OfType<PendingGameDetection>()
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Name) &&
                    !string.IsNullOrWhiteSpace(item.SourceType))
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var item = group.First();
                    var processes = (item.Processes ?? [])
                        .Where(process => !string.IsNullOrWhiteSpace(process))
                        .Where(process => !string.IsNullOrWhiteSpace(NameNormalizer.NormalizeProcessName(process)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    return item with { Processes = processes };
                })
                .ToArray();
            return settings with
            {
                PendingDetections = pending,
                IgnoredDetectionKeys = (settings.IgnoredDetectionKeys ?? [])
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
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
        TrySave(settings);
    }

    public bool TrySave(AppSettings settings)
    {
        try
        {
            ReminderStore.AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonProtocol.Options));
            return true;
        }
        catch (IOException)
        {
            // Settings are a convenience cache. A transient local write failure
            // must not prevent the iCloud-backed reminder app from starting.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Continue with the settings already loaded for this session.
            return false;
        }
    }
}
