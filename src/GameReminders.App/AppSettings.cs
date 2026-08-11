using System.Text.Json;
using GameReminders.Core;

namespace GameReminders.App;

public sealed record AppSettings
{
    public string? ICloudRoot { get; init; }
    public IReadOnlyList<PendingGameDetection> PendingDetections { get; init; } = [];
    public IReadOnlyList<PendingGameDetection> IgnoredDetections { get; init; } = [];
    public IReadOnlyList<string> IgnoredDetectionKeys { get; init; } = [];
    public IReadOnlyList<SuppressedSteamGame> SuppressedSteamGames { get; init; } = [];
    public IReadOnlyList<string> UnreviewedGameIds { get; init; } = [];
}

public sealed record SuppressedSteamGame
{
    public string AppId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed record PendingGameDetection
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Processes { get; init; } = [];
    public IReadOnlyList<string> CandidateProcesses { get; init; } = [];
    public bool RequiresExecutableReview { get; init; }
    public string SourceType { get; init; } = string.Empty;
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
            var pending = NormalizeDetections(settings.PendingDetections);
            var ignored = NormalizeDetections(settings.IgnoredDetections);
            return settings with
            {
                PendingDetections = pending,
                IgnoredDetections = ignored,
                IgnoredDetectionKeys = (settings.IgnoredDetectionKeys ?? [])
                    .Concat(ignored.Select(item => item.Key))
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SuppressedSteamGames = (settings.SuppressedSteamGames ?? [])
                    .OfType<SuppressedSteamGame>()
                    .Where(game => !string.IsNullOrWhiteSpace(game.AppId) && !string.IsNullOrWhiteSpace(game.Name))
                    .GroupBy(game => game.AppId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray(),
                UnreviewedGameIds = (settings.UnreviewedGameIds ?? [])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Local settings.json is malformed and was preserved. Correct or restore the file, then restart Game Reminders.",
                exception);
        }
    }

    private static IReadOnlyList<PendingGameDetection> NormalizeDetections(
        IReadOnlyList<PendingGameDetection>? detections) =>
        (detections ?? [])
            .OfType<PendingGameDetection>()
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) &&
                !string.IsNullOrWhiteSpace(item.Name) &&
                !string.IsNullOrWhiteSpace(item.SourceType))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var item = items
                    .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.AppId))
                    .ThenBy(candidate => candidate.DetectedAt)
                    .First();
                var processes = items
                    .SelectMany(candidate => candidate.Processes ?? [])
                    .Where(process => !string.IsNullOrWhiteSpace(process))
                    .Where(process => !string.IsNullOrWhiteSpace(NameNormalizer.NormalizeProcessName(process)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var candidates = items
                    .SelectMany(candidate => candidate.CandidateProcesses ?? [])
                    .Where(process => !string.IsNullOrWhiteSpace(process))
                    .Where(process => !string.IsNullOrWhiteSpace(NameNormalizer.NormalizeExecutableIdentity(process)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return item with
                {
                    Processes = processes,
                    CandidateProcesses = candidates,
                    RequiresExecutableReview = items.Any(candidate => candidate.RequiresExecutableReview),
                    DetectedAt = items.Min(candidate => candidate.DetectedAt)
                };
            })
            .ToArray();

    public void Save(AppSettings settings) => TrySave(settings);

    public bool TrySave(AppSettings settings)
    {
        try
        {
            ReminderStore.AtomicWrite(_settingsPath, JsonSerializer.Serialize(settings, JsonProtocol.Options));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
