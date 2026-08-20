using Microsoft.Win32;
using GameReminders.Core;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GameReminders.App;

public sealed class SteamGameDiscovery
{
    private static readonly string[] ExcludedExecutableFragments =
    [
        "anticheat", "battleye", "crashreport", "crashpad", "easyanticheat", "redist",
        "unitycrashhandler", "unins", "vc_redist"
    ];
    private static readonly string[] DeprioritizedExecutableFragments =
    [
        "bootstrap", "dedicatedserver", "editor", "launcher", "level editor", "server", "setup", "updater"
    ];
    private static readonly Regex QuotedPair = new(
        "\\\"(?<key>[^\\\"]+)\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Valve's EAppState bit meaning the payload is on disk. Steam sets it when an
    // install finishes and leaves it set through later updates, so its absence means
    // Steam is still downloading, staging, or committing files.
    private const long StateFullyInstalled = 4;
    private const string ManifestPrefix = "appmanifest_";

    public SteamDiscoveryResult Discover() => Discover(null);

    /// <summary>
    /// Enumerates installed Steam games. When <paramref name="appIds"/> is supplied only
    /// those apps are inspected, which keeps an install-completion poll from re-walking
    /// every library for executables.
    /// </summary>
    /// <remarks>
    /// The result reports whether every library was enumerated without error. Only a
    /// complete scan may be used to conclude that a missing app id means the game is
    /// gone rather than that Steam was temporarily unreadable.
    /// </remarks>
    public SteamDiscoveryResult Discover(IReadOnlySet<string>? appIds)
    {
        var steamRoot = FindSteamRoot();
        if (steamRoot is null)
        {
            return new SteamDiscoveryResult([], LibrariesFullyEnumerated: false);
        }

        var fullyEnumerated = true;
        var libraries = FindLibraries(steamRoot, out var librariesResolved)
            .Append(steamRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        fullyEnumerated &= librariesResolved;
        var results = new Dictionary<string, PendingGameDetection>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                // A library Steam still lists but whose contents we cannot see. Treat the
                // scan as incomplete rather than concluding its games were uninstalled.
                fullyEnumerated = false;
                continue;
            }

            var manifests = EnumerateManifestFiles(steamApps, out var manifestsListed);
            fullyEnumerated &= manifestsListed;
            foreach (var manifest in manifests)
            {
                if (!ShouldReadManifest(manifest, appIds))
                {
                    continue;
                }

                try
                {
                    var values = ParsePairs(File.ReadAllText(manifest))
                        .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);
                    var detection = CreateDetection(values, steamApps);
                    if (detection is not null)
                    {
                        results[detection.Key] = detection;
                    }
                }
                catch (IOException)
                {
                    // Steam or a sync/backup tool may be updating a manifest. Retry later.
                    fullyEnumerated = false;
                }
                catch (UnauthorizedAccessException)
                {
                    // A library can be present but temporarily inaccessible.
                    fullyEnumerated = false;
                }
            }
        }

        return new SteamDiscoveryResult(
            results.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            fullyEnumerated);
    }

    internal static IReadOnlyList<string> EnumerateManifestFiles(
        string steamApps,
        out bool succeeded,
        Func<string, string, IEnumerable<string>>? enumerateFiles = null)
    {
        try
        {
            var manifests = (enumerateFiles ?? Directory.EnumerateFiles)(steamApps, "appmanifest_*.acf").ToArray();
            succeeded = true;
            return manifests;
        }
        catch (IOException)
        {
            succeeded = false;
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            succeeded = false;
            return [];
        }
    }

    internal static bool ShouldReadManifest(string manifestPath, IReadOnlySet<string>? appIds)
    {
        if (appIds is null)
        {
            return true;
        }

        var filename = Path.GetFileNameWithoutExtension(manifestPath);
        return filename.StartsWith(ManifestPrefix, StringComparison.OrdinalIgnoreCase) &&
            appIds.Contains(filename[ManifestPrefix.Length..]);
    }

    // Discovery never yields NotInstalled: a detection exists only because a manifest does.
    internal static InstallState ResolveInstallState(IReadOnlyDictionary<string, string> values, bool hasExecutables)
    {
        if (values.TryGetValue("StateFlags", out var rawFlags) &&
            long.TryParse(rawFlags.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var flags))
        {
            return (flags & StateFullyInstalled) == 0 ? InstallState.Installing : InstallState.Installed;
        }

        // Without a readable state, fall back to the naive signal: a manifest whose
        // install directory holds no executables is treated as still installing.
        return hasExecutables ? InstallState.Installed : InstallState.Installing;
    }

    internal static PendingGameDetection? CreateDetection(
        IReadOnlyDictionary<string, string> values,
        string steamApps,
        Func<string, IReadOnlyList<string>>? findLikelyExecutables = null)
    {
        if (!values.TryGetValue("appid", out var rawAppId) ||
            !values.TryGetValue("name", out var rawName) ||
            !values.TryGetValue("installdir", out var rawInstallDir))
        {
            return null;
        }

        var appId = rawAppId.Trim();
        var name = rawName.Trim();
        var installDir = rawInstallDir.Trim();
        if (!ulong.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var commonRoot = Path.Combine(steamApps, "common");
        var gameRoot = Path.Combine(commonRoot, installDir);
        var candidates = (findLikelyExecutables ?? (path => FindLikelyExecutables(path)))(gameRoot)
            .Select(path => ToPortableExecutablePath(commonRoot, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var installState = ResolveInstallState(values, candidates.Length > 0);
        var installing = installState == InstallState.Installing;
        var selection = SelectExecutable(name, candidates);
        return new PendingGameDetection
        {
            Key = $"steam:{appId}",
            Name = name,
            Processes = selection.Process is null ? [] : [selection.Process],
            CandidateProcesses = candidates,
            RequiresExecutableReview = !installing && selection.Process is null,
            InstallState = installState,
            SourceType = "steam",
            AppId = appId
        };
    }

    private static string? FindSteamRoot()
    {
        foreach (var keyPath in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
        {
            var value = Registry.GetValue(keyPath, "SteamPath", null) as string
                ?? Registry.GetValue(keyPath, "InstallPath", null) as string;
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                return value.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> FindLibraries(string steamRoot, out bool succeeded)
    {
        var path = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(path))
        {
            // Steam without a library file keeps its games under the root library, which
            // is a complete answer rather than a failure to look.
            succeeded = true;
            return [];
        }

        try
        {
            var listed = ParsePairs(File.ReadAllText(path))
                .Where(pair => string.Equals(pair.Key, "path", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value.Replace("\\\\", "\\"))
                .ToArray();
            var present = listed.Where(Directory.Exists).ToArray();
            // A listed library that is not mounted, such as an external drive, hides its
            // games from this scan.
            succeeded = present.Length == listed.Length;
            return present;
        }
        catch (IOException)
        {
            succeeded = false;
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            succeeded = false;
            return [];
        }
    }

    internal static IReadOnlyList<string> FindLikelyExecutables(
        string gameRoot,
        Func<string, string, EnumerationOptions, IEnumerable<string>>? enumerateFiles = null)
    {
        if (enumerateFiles is null && !Directory.Exists(gameRoot))
        {
            return [];
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            return (enumerateFiles ?? Directory.EnumerateFiles)(gameRoot, "*.exe", options)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Engine{Path.DirectorySeparatorChar}Binaries{Path.DirectorySeparatorChar}ThirdParty{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !ExcludedExecutableFragments.Any(fragment => Path.GetFileName(path).Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static string ToPortableExecutablePath(string commonRoot, string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(normalized))
        {
            return normalized;
        }

        try
        {
            var relative = Path.GetRelativePath(commonRoot, normalized);
            return relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || relative == ".."
                ? normalized
                : relative;
        }
        catch (ArgumentException)
        {
            return normalized;
        }
    }

    internal static ExecutableSelection SelectExecutable(string gameName, IReadOnlyList<string> candidates)
    {
        var scored = candidates
            .Select(path => new ExecutableSelection(path, ScoreExecutable(gameName, path)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Process, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scored.Length == 0)
        {
            return new ExecutableSelection(null, 0);
        }

        var best = scored[0];
        var confident = best.Score >= 70 &&
            (scored.Length == 1 || best.Score - scored[1].Score >= 15);
        return confident
            ? best
            : new ExecutableSelection(null, best.Score);
    }

    private static int ScoreExecutable(string gameName, string path)
    {
        var filename = Path.GetFileNameWithoutExtension(path);
        var normalizedName = GameReminders.Core.NameNormalizer.Normalize(gameName);
        var normalizedFile = GameReminders.Core.NameNormalizer.Normalize(filename);
        var score = 0;
        if (string.Equals(normalizedName, normalizedFile, StringComparison.Ordinal))
        {
            score += 120;
        }
        else if (normalizedName.Length >= 5 && normalizedFile.Contains(normalizedName, StringComparison.Ordinal))
        {
            score += 95;
        }
        else if (normalizedFile.Length >= 5 && normalizedName.Contains(normalizedFile, StringComparison.Ordinal))
        {
            score += 75;
        }

        if (filename.Contains("shipping", StringComparison.OrdinalIgnoreCase)) score += 15;
        if (IsInstallRootExecutable(path)) score += 30;
        if (DeprioritizedExecutableFragments.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase))) score -= 60;
        return score;
    }

    private static bool IsInstallRootExecutable(string path) => path
        .Replace('/', '\\')
        .Split('\\', StringSplitOptions.RemoveEmptyEntries)
        .Length == 2;

    internal static IReadOnlyList<KeyValuePair<string, string>> ParsePairs(string text) =>
        QuotedPair.Matches(text)
            .Select(match => new KeyValuePair<string, string>(match.Groups["key"].Value, match.Groups["value"].Value))
            .ToArray();
}

internal sealed record ExecutableSelection(string? Process, int Score);

/// <summary>
/// Games Steam reported, plus whether every library was read without error. Absence of an
/// app id only means the game is gone when <paramref name="LibrariesFullyEnumerated"/> is true.
/// </summary>
internal sealed record SteamDiscoveryResult(
    IReadOnlyList<PendingGameDetection> Games,
    bool LibrariesFullyEnumerated);
