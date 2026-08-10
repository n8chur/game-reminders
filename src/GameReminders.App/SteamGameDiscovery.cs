using Microsoft.Win32;
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

    public IReadOnlyList<PendingGameDetection> Discover()
    {
        var steamRoot = FindSteamRoot();
        if (steamRoot is null)
        {
            return [];
        }

        var libraries = FindLibraries(steamRoot).Append(steamRoot).Distinct(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, PendingGameDetection>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifest in EnumerateManifestFiles(steamApps))
            {
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
                }
                catch (UnauthorizedAccessException)
                {
                    // A library can be present but temporarily inaccessible.
                }
            }
        }

        return results.Values.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static IReadOnlyList<string> EnumerateManifestFiles(
        string steamApps,
        Func<string, string, IEnumerable<string>>? enumerateFiles = null)
    {
        try
        {
            return (enumerateFiles ?? Directory.EnumerateFiles)(steamApps, "appmanifest_*.acf").ToArray();
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
        var candidates = (findLikelyExecutables ?? FindLikelyExecutables)(gameRoot)
            .Select(path => ToPortableExecutablePath(commonRoot, path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selection = SelectExecutable(name, candidates);
        return new PendingGameDetection
        {
            Key = $"steam:{appId}",
            Name = name,
            Processes = selection.Process is null ? [] : [selection.Process],
            CandidateProcesses = candidates,
            RequiresExecutableReview = selection.Process is null,
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

    private static IEnumerable<string> FindLibraries(string steamRoot)
    {
        var path = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return ParsePairs(File.ReadAllText(path))
                .Where(pair => string.Equals(pair.Key, "path", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value.Replace("\\\\", "\\"))
                .Where(Directory.Exists)
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

    private static IReadOnlyList<string> FindLikelyExecutables(string gameRoot)
    {
        if (!Directory.Exists(gameRoot))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(gameRoot, "*.exe", SearchOption.AllDirectories)
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
        if (DeprioritizedExecutableFragments.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase))) score -= 60;
        return score;
    }

    internal static IReadOnlyList<KeyValuePair<string, string>> ParsePairs(string text) =>
        QuotedPair.Matches(text)
            .Select(match => new KeyValuePair<string, string>(match.Groups["key"].Value, match.Groups["value"].Value))
            .ToArray();
}

internal sealed record ExecutableSelection(string? Process, int Score);
