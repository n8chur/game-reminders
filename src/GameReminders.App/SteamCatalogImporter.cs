using GameReminders.Core;

namespace GameReminders.App;

internal sealed record SteamImportResult(GameCatalog Catalog, IReadOnlyList<GameDefinition> AddedGames);

internal static class SteamCatalogImporter
{
    public static SteamImportResult Import(
        GameCatalog catalog,
        IEnumerable<PendingGameDetection> detections)
    {
        var games = catalog.Games.ToList();
        var ids = games.Select(game => game.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var appIds = games
            .Select(game => game.Source?.AppId)
            .Where(appId => !string.IsNullOrWhiteSpace(appId))
            .Select(appId => appId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processes = games
            .SelectMany(game => game.Processes)
            .Select(NameNormalizer.NormalizeProcessName)
            .Where(process => !string.IsNullOrWhiteSpace(process))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<GameDefinition>();

        foreach (var detection in detections)
        {
            if (!string.Equals(detection.SourceType, "steam", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(detection.AppId) ||
                string.IsNullOrWhiteSpace(detection.Name))
            {
                continue;
            }

            var id = $"steam-{detection.AppId}";
            var detectionProcesses = detection.Processes
                .Where(process => !string.IsNullOrWhiteSpace(process))
                .Where(process => !string.IsNullOrWhiteSpace(NameNormalizer.NormalizeProcessName(process)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ids.Contains(id) || appIds.Contains(detection.AppId))
            {
                continue;
            }

            var availableProcesses = detectionProcesses
                .Where(process => !processes.Contains(NameNormalizer.NormalizeProcessName(process)))
                .ToArray();
            var normalizedProcesses = availableProcesses
                .Select(NameNormalizer.NormalizeProcessName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var game = new GameDefinition
            {
                Id = id,
                Name = detection.Name,
                Processes = availableProcesses,
                Source = new GameSource { Type = "steam", AppId = detection.AppId }
            };
            games.Add(game);
            added.Add(game);
            ids.Add(id);
            appIds.Add(detection.AppId);
            processes.UnionWith(normalizedProcesses);
        }

        return new SteamImportResult(
            catalog with
            {
                Games = games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray()
            },
            added);
    }
}
