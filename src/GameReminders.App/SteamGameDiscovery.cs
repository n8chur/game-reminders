using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace GameReminders.App;

public sealed class SteamGameDiscovery
{
    private static readonly string[] ExcludedExecutableFragments =
    [
        "crashreport", "easyanticheat", "unitycrashhandler", "unins", "vc_redist"
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
        if (!values.TryGetValue("appid", out var appId) || string.IsNullOrWhiteSpace(appId) ||
            !values.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name) ||
            !values.TryGetValue("installdir", out var installDir) || string.IsNullOrWhiteSpace(installDir))
        {
            return null;
        }

        var gameRoot = Path.Combine(steamApps, "common", installDir);
        return new PendingGameDetection
        {
            Key = $"steam:{appId}",
            Name = name,
            Processes = (findLikelyExecutables ?? FindLikelyExecutables)(gameRoot),
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
                .Select(path => Path.GetFileName(path))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
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

    internal static IReadOnlyList<KeyValuePair<string, string>> ParsePairs(string text) =>
        QuotedPair.Matches(text)
            .Select(match => new KeyValuePair<string, string>(match.Groups["key"].Value, match.Groups["value"].Value))
            .ToArray();
}
