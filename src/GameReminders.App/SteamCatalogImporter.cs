using GameReminders.Core;

namespace GameReminders.App;

internal sealed record SteamImportResult(
    GameCatalog Catalog,
    IReadOnlyList<GameDefinition> AddedGames,
    IReadOnlyList<GameDefinition> UpdatedGames,
    IReadOnlyList<GameDefinition> GamesNeedingExecutableReview,
    IReadOnlyList<GameDefinition> InstallingGames,
    IReadOnlyList<GameDefinition> CompletedInstalls,
    IReadOnlyList<GameDefinition> RetractedGames);

internal static class SteamCatalogImporter
{
    /// <param name="librariesFullyEnumerated">
    /// Whether the scan read every Steam library without error. Reconciliation only runs
    /// when it did, so an unmounted drive or an unreadable manifest never looks like an
    /// uninstall.
    /// </param>
    /// <param name="scannedAppIds">
    /// App ids the scan actually looked for, or null for a full scan. A targeted poll must
    /// not draw conclusions about apps it never inspected.
    /// </param>
    public static SteamImportResult Import(
        GameCatalog catalog,
        IEnumerable<PendingGameDetection> detections,
        IEnumerable<string>? suppressedAppIds = null,
        bool librariesFullyEnumerated = false,
        IReadOnlySet<string>? scannedAppIds = null)
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
        var retracted = new List<GameDefinition>();
        var detectedAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            detectedAppIds.Add(detection.AppId!);
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
                // Executables and install state are refreshed separately: the first only
                // applies to unresolved games, while the second must also clear NotInstalled
                // from a fully configured game the user just reinstalled.
                var refreshed = ApplyInstallState(
                    RefreshUnresolvedGame(existing, detection, detectionProcesses, processOwners),
                    detection.InstallState);
                if (refreshed != existing)
                {
                    games[existingIndex] = refreshed;
                    updated.Add(refreshed);
                    if (existing.Source?.InstallState == InstallState.Installing &&
                        refreshed.Source?.InstallState != InstallState.Installing)
                    {
                        completedInstalls.Add(refreshed);
                    }

                    foreach (var process in refreshed.Processes)
                    {
                        processOwners[NameNormalizer.NormalizeExecutableIdentity(process)] = refreshed.Id;
                    }
                }

                if (games[existingIndex].Source?.InstallState == InstallState.Installing)
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
                    RequiresExecutableReview = detection.InstallState != InstallState.Installing &&
                        (detection.RequiresExecutableReview || availableProcesses.Length == 0),
                    InstallState = detection.InstallState,
                    ExecutableCandidates = detection.CandidateProcesses
                }
            };
            games.Add(game);
            added.Add(game);
            if (game.Source.RequiresExecutableReview)
            {
                needingReview.Add(game);
            }

            if (game.Source.InstallState == InstallState.Installing)
            {
                installing.Add(game);
            }
            foreach (var process in normalizedProcesses)
            {
                processOwners[process] = id;
            }
        }

        if (librariesFullyEnumerated)
        {
            Reconcile(games, detectedAppIds, scannedAppIds, suppressed, updated, retracted);
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
            completedInstalls,
            retracted);
    }

    /// <summary>
    /// Resolves steam games the scan covered but Steam did not report. An entry that was
    /// only ever an in-progress install is retracted, because nothing about it was
    /// user-configured; anything else is flagged so its executable mapping survives a
    /// reinstall.
    /// </summary>
    private static void Reconcile(
        List<GameDefinition> games,
        IReadOnlySet<string> detectedAppIds,
        IReadOnlySet<string>? scannedAppIds,
        IReadOnlySet<string> suppressedAppIds,
        List<GameDefinition> updated,
        List<GameDefinition> retracted)
    {
        for (var index = games.Count - 1; index >= 0; index--)
        {
            var game = games[index];
            var source = game.Source;
            if (source is null ||
                !string.Equals(source.Type, "steam", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(source.AppId) ||
                detectedAppIds.Contains(source.AppId) ||
                suppressedAppIds.Contains(source.AppId) ||
                (scannedAppIds is not null && !scannedAppIds.Contains(source.AppId)))
            {
                continue;
            }

            if (source.InstallState == InstallState.Installing && game.Processes.Count == 0)
            {
                // Retraction is not suppression: the id is derived from the app id, so a
                // reinstall re-adds the same entry and any reminder relinks itself.
                games.RemoveAt(index);
                retracted.Add(game);
                continue;
            }

            if (source.InstallState == InstallState.NotInstalled)
            {
                continue;
            }

            var flagged = game with { Source = source with { InstallState = InstallState.NotInstalled } };
            games[index] = flagged;
            updated.Add(flagged);
        }
    }

    private static GameDefinition ApplyInstallState(GameDefinition game, InstallState state)
    {
        var source = game.Source;
        return source is null || source.InstallState == state
            ? game
            : game with { Source = source with { InstallState = state } };
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
            !(existingSource.RequiresExecutableReview || existingSource.InstallState != InstallState.Installed))
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
        // Install state itself is owned by ApplyInstallState; only the review flag depends on it.
        var source = existingSource with
        {
            RequiresExecutableReview = selected.Length == 0 && detection.InstallState != InstallState.Installing,
            ExecutableCandidates = candidates
        };

        if (selected.Length == 0 &&
            source.RequiresExecutableReview == existingSource.RequiresExecutableReview &&
            candidates.SequenceEqual(existingSource.ExecutableCandidates, StringComparer.OrdinalIgnoreCase))
        {
            return existing;
        }

        return existing with { Processes = selected, Source = source };
    }
}
