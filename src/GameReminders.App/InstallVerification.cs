using GameReminders.Core;

namespace GameReminders.App;

/// <summary>
/// A catalog whose non-Steam install states have been re-derived, plus the entries whose
/// state actually moved.
/// </summary>
internal sealed record InstallVerificationResult(
    GameCatalog Catalog,
    IReadOnlyList<GameDefinition> ChangedGames);

/// <summary>
/// Derives install state for games no launcher reports on. Steam entries are owned by
/// <see cref="SteamCatalogImporter"/>, which reconciles them against Steam itself; every
/// other source stores an absolute executable path when it is created, so whether that
/// file is on disk is the whole answer.
/// </summary>
internal static class InstallVerification
{
    /// <summary>Re-derives install state for every non-Steam game in the catalog.</summary>
    public static InstallVerificationResult Verify(
        GameCatalog catalog,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? volumeExists = null)
    {
        var games = catalog.Games.ToList();
        var changed = new List<GameDefinition>();
        for (var index = 0; index < games.Count; index++)
        {
            var verified = Verify(games[index], fileExists, volumeExists);
            if (verified != games[index])
            {
                games[index] = verified;
                changed.Add(verified);
            }
        }

        return changed.Count == 0
            ? new InstallVerificationResult(catalog, [])
            : new InstallVerificationResult(catalog with { Games = games }, changed);
    }

    /// <summary>
    /// Returns the game carrying the install state its files imply, or the same value when
    /// nothing checkable answers the question.
    /// </summary>
    public static GameDefinition Verify(
        GameDefinition game,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? volumeExists = null)
    {
        Func<string, bool> exists = fileExists ?? File.Exists;
        Func<string, bool> volume = volumeExists ?? Directory.Exists;
        if (Resolve(game, exists, volume) is not { } state)
        {
            return game;
        }

        var source = game.Source;
        if (source is null)
        {
            // A hand-written entry with no source still deserves the flag, but writing a
            // source block just to record the default would churn the file for nothing.
            return state == InstallState.Installed
                ? game
                : game with { Source = new GameSource { Type = "manual", InstallState = state } };
        }

        return source.InstallState == state
            ? game
            : game with { Source = source with { InstallState = state } };
    }

    /// <summary>
    /// The evidence rule: present files mean installed, uniformly missing files mean
    /// uninstalled, and no checkable mapping means no conclusion at all.
    /// </summary>
    internal static InstallState? Resolve(
        GameDefinition game,
        Func<string, bool> fileExists,
        Func<string, bool> volumeExists)
    {
        if (IsSteam(game.Source))
        {
            return null;
        }

        var checkable = 0;
        foreach (var process in game.Processes)
        {
            if (!IsCheckablePath(process, volumeExists))
            {
                continue;
            }

            checkable++;
            if (fileExists(process.Trim()))
            {
                // One surviving executable is enough: a game may map several and ship
                // only some of them on a given install.
                return InstallState.Installed;
            }
        }

        return checkable == 0 ? null : InstallState.NotInstalled;
    }

    internal static bool IsSteam(GameSource? source) =>
        string.Equals(source?.Type?.Trim(), "steam", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A mapping answers the question only when it names a rooted path whose volume is
    /// attached. A legacy filename-only mapping and a path on a disconnected drive both
    /// mean "no evidence", never "uninstalled".
    /// </summary>
    internal static bool IsCheckablePath(string process, Func<string, bool> volumeExists)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            return false;
        }

        var trimmed = process.Trim();
        if (!NameNormalizer.IsExecutablePath(trimmed) || !Path.IsPathRooted(trimmed))
        {
            return false;
        }

        var root = Path.GetPathRoot(trimmed);
        return !string.IsNullOrWhiteSpace(root) && volumeExists(root);
    }
}
