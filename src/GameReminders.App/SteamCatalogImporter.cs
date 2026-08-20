using GameReminders.Core;

namespace GameReminders.App;

internal sealed record SteamImportResult(
    GameCatalog Catalog,
    IReadOnlyList<GameDefinition> AddedGames,
    IReadOnlyList<GameDefinition> UpdatedGames,
    IReadOnlyList<GameDefinition> GamesNeedingExecutableReview,
    IReadOnlyList<GameDefinition> InstallingGames,
    IReadOnlyList<GameDefinition> CompletedInstalls);

internal static class SteamCatalogImporter
{
    public static SteamImportResult Import(
        GameCatalog catalog,
        IEnumerable<PendingGameDetection> detections,
        IEnumerable<string>? suppressedAppIds = null)
    {
        var games = catalog.Games.ToList();
        var processOwners = games
            .SelectMany(game => game.Processes.Select(process => new
            {
                Process = NameNormalizer.NormalizeExecutableIdentity(process),
                game.Id
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Process))
            .GroupBy(item => item.Process, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var added = new List<GameDefinition>();
        var updated = new List<GameDefinition>();
        var needingReview = new List<GameDefinition>();
        var installing = new List<GameDefinition>();
        var completedInstalls = new List<GameDefinition>();
        var suppressed = (suppressedAppIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var detection in detections)
        {
            if (!string.Equals(detection.SourceType, "steam", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(detection.AppId) ||
                string.IsNullOrWhiteSpace(detection.Name) ||
                suppressed.Contains(detection.AppId))
            {
                continue;
            }

            var id = $"steam-{detection.AppId}";
            var detectionProcesses = detection.Processes
                .Where(process => !string.IsNullOrWhiteSpace(process))
                .Where(process => !string.IsNullOrWhiteSpace(NameNormalizer.NormalizeExecutableIdentity(process)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var existingIndex = games.FindIndex(game =>
                string.Equals(game.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(game.Source?.AppId, detection.AppId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                var existing = games[existingIndex];
                var refreshed = RefreshUnresolvedGame(existing, detection, detectionProcesses, processOwners);
                if (refreshed != existing)
                {
                    games[existingIndex] = refreshed;
                    updated.Add(refreshed);
                    if (existing.Source?.InstallationPending == true &&
                        refreshed.Source?.InstallationPending != true)
                    {
                        completedInstalls.Add(refreshed);
                    }

                    foreach (var process in refreshed.Processes)
                    {
                        processOwners[NameNormalizer.NormalizeExecutableIdentity(process)] = refreshed.Id;
                    }
                }

                if (games[existingIndex].Source?.InstallationPending == true)
                {
                    installing.Add(games[existingIndex]);
                }

                continue;
            }

            var availableProcesses = detectionProcesses
                .Where(process => !processOwners.ContainsKey(NameNormalizer.NormalizeExecutableIdentity(process)))
                .ToArray();
            var normalizedProcesses = availableProcesses
                .Select(NameNormalizer.NormalizeExecutableIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var game = new GameDefinition
            {
                Id = id,
                Name = detection.Name,
                Processes = availableProcesses,
                Source = new GameSource
                {
                    Type = "steam",
                    AppId = detection.AppId,
                    RequiresExecutableReview = !detection.InstallationPending &&
                        (detection.RequiresExecutableReview || availableProcesses.Length == 0),
                    InstallationPending = detection.InstallationPending,
                    ExecutableCandidates = detection.CandidateProcesses
                }
            };
            games.Add(game);
            added.Add(game);
            if (game.Source.RequiresExecutableReview)
            {
                needingReview.Add(game);
            }

            if (game.Source.InstallationPending)
            {
                installing.Add(game);
            }
            foreach (var process in normalizedProcesses)
            {
                processOwners[process] = id;
            }
        }

        return new SteamImportResult(
            catalog with
            {
                Games = games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()
            },
            added,
            updated,
            needingReview,
            installing,
            completedInstalls);
    }

    private static GameDefinition RefreshUnresolvedGame(
        GameDefinition existing,
        PendingGameDetection detection,
        IReadOnlyList<string> detectionProcesses,
        IReadOnlyDictionary<string, string> processOwners)
    {
        var existingSource = existing.Source;
        if (existingSource is null ||
            existing.Processes.Count > 0 ||
            !(existingSource.RequiresExecutableReview || existingSource.InstallationPending))
        {
            return existing;
        }

        var selected = detectionProcesses
            .Where(process => !processOwners.TryGetValue(NameNormalizer.NormalizeExecutableIdentity(process), out var ownerId) ||
                string.Equals(ownerId, existing.Id, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToArray();
        var candidates = detection.CandidateProcesses.Count > 0
            ? detection.CandidateProcesses
            : existingSource.ExecutableCandidates;
        var source = existingSource with
        {
            RequiresExecutableReview = selected.Length == 0 && !detection.InstallationPending,
            InstallationPending = detection.InstallationPending,
            ExecutableCandidates = candidates
        };

        if (selected.Length == 0 &&
            source.RequiresExecutableReview == existingSource.RequiresExecutableReview &&
            source.InstallationPending == existingSource.InstallationPending &&
            candidates.SequenceEqual(existingSource.ExecutableCandidates, StringComparer.OrdinalIgnoreCase))
        {
            return existing;
        }

        return existing with { Processes = selected, Source = source };
    }
}
