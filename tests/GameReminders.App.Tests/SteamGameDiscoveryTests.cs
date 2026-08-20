using GameReminders.Core;
namespace GameReminders.App.Tests;

public sealed class SteamGameDiscoveryTests
{
    [Fact]
    public void ParsePairsReadsSteamKeyValuesAndEscapedLibraryPath()
    {
        const string text = """
            "libraryfolders"
            {
                "0"
                {
                    "path" "D:\\SteamLibrary"
                }
            }
            """;

        var pair = Assert.Single(SteamGameDiscovery.ParsePairs(text));

        Assert.Equal("path", pair.Key);
        Assert.Equal(@"D:\\SteamLibrary", pair.Value);
    }

    [Fact]
    public void ManifestEnumerationFailureSkipsInaccessibleLibrary()
    {
        var manifests = SteamGameDiscovery.EnumerateManifestFiles(
            @"D:\\SteamLibrary\\steamapps",
            out var succeeded,
            (_, _) => ThrowDuringEnumeration());

        Assert.Empty(manifests);
        Assert.False(succeeded);
    }

    [Fact]
    public void ExecutableEnumerationSkipsInaccessibleSubdirectoriesAndReparsePoints()
    {
        EnumerationOptions? requestedOptions = null;

        var executables = SteamGameDiscovery.FindLikelyExecutables(
            @"D:\\SteamLibrary\\steamapps\\common\\Test Game",
            (_, _, options) =>
            {
                requestedOptions = options;
                return [@"D:\\SteamLibrary\\steamapps\\common\\Test Game\\TestGame.exe"];
            });

        Assert.Single(executables);
        Assert.NotNull(requestedOptions);
        Assert.True(requestedOptions.RecurseSubdirectories);
        Assert.True(requestedOptions.IgnoreInaccessible);
        Assert.Equal(FileAttributes.ReparsePoint, requestedOptions.AttributesToSkip);
    }

    [Theory]
    [InlineData("", "Test Game", "TestGame")]
    [InlineData("123", "Test Game", "")]
    [InlineData("123", "", "TestGame")]
    public void EmptyRequiredManifestValuesAreRejected(string appId, string name, string installDir)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appid"] = appId,
            ["name"] = name,
            ["installdir"] = installDir
        };

        var detection = SteamGameDiscovery.CreateDetection(values, @"D:\\SteamLibrary\\steamapps", _ => []);

        Assert.Null(detection);
    }

    [Fact]
    public void ManifestValuesAreTrimmedBeforeStableIdentityIsCreated()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appid"] = " 123 ",
            ["name"] = " Test Game ",
            ["installdir"] = " TestGame "
        };

        var detection = SteamGameDiscovery.CreateDetection(values, @"D:\\SteamLibrary\\steamapps", _ => []);

        Assert.NotNull(detection);
        Assert.Equal("steam:123", detection.Key);
        Assert.Equal("123", detection.AppId);
        Assert.Equal("Test Game", detection.Name);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("123.0")]
    [InlineData("-123")]
    public void NonNumericSteamAppIdsAreRejected(string appId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appid"] = appId,
            ["name"] = "Test Game",
            ["installdir"] = "TestGame"
        };

        Assert.Null(SteamGameDiscovery.CreateDetection(values, @"D:\\SteamLibrary\\steamapps", _ => []));
    }

    [Fact]
    public void ClearNameMatchSelectsOnlyTheLikelyGameExecutable()
    {
        var selection = SteamGameDiscovery.SelectExecutable("Destiny 2",
        [
            @"Destiny 2\destiny2.exe",
            @"Destiny 2\Tools\LevelEditor.exe",
            @"Destiny 2\Launcher.exe"
        ]);

        Assert.Equal(@"Destiny 2\destiny2.exe", selection.Process);
    }

    [Theory]
    [InlineData("Everwind", @"Everwind\Everwind.exe", @"Everwind\Binaries\Win64\Everwind-Win64-Shipping.exe")]
    [InlineData("Mistfall Hunter", @"Mistfall Hunter\MistfallHunter.exe", @"Mistfall Hunter\Binaries\Win64\MistfallHunter-Win64-Shipping.exe")]
    [InlineData("Palworld", @"Palworld\Palworld.exe", @"Palworld\Pal\Binaries\Win64\Palworld-Win64-Shipping.exe")]
    public void RootExactNameMatchWinsOverNestedShippingExecutable(
        string gameName,
        string expected,
        string nestedCandidate)
    {
        var selection = SteamGameDiscovery.SelectExecutable(gameName, [expected, nestedCandidate]);

        Assert.Equal(expected, selection.Process);
    }

    [Fact]
    public void GenericExecutableCandidatesRequireReview()
    {
        var selection = SteamGameDiscovery.SelectExecutable("Two Unreal Games",
        [
            @"Two Unreal Games\Binaries\Win64\Game-Win64-Shipping.exe",
            @"Two Unreal Games\Launcher.exe"
        ]);

        Assert.Null(selection.Process);
    }

    [Theory]
    [InlineData("2")]          // StateUpdateRequired
    [InlineData("1024")]       // StateUpdateStarted
    [InlineData("1026")]       // StateUpdateRequired | StateUpdateStarted
    [InlineData("1048576")]    // StateDownloading
    [InlineData("2097152")]    // StateStaging
    public void StateFlagsWithoutFullyInstalledMeanInstallationIsPending(string stateFlags)
    {
        var values = Manifest(stateFlags);

        Assert.True(IsInstalling(values, hasExecutables: false));
        Assert.True(IsInstalling(values, hasExecutables: true));
    }

    [Theory]
    [InlineData("4")]          // StateFullyInstalled
    [InlineData("6")]          // StateFullyInstalled | StateUpdateRequired
    [InlineData("260")]        // StateFullyInstalled | StateUpdateRunning
    [InlineData("1028")]       // StateFullyInstalled | StateUpdateStarted
    public void StateFlagsWithFullyInstalledMeanInstallationIsComplete(string stateFlags)
    {
        var values = Manifest(stateFlags);

        Assert.False(IsInstalling(values, hasExecutables: false));
        Assert.False(IsInstalling(values, hasExecutables: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void UnreadableStateFlagsFallBackToTheExecutableCount(string? stateFlags)
    {
        var values = Manifest(stateFlags);

        Assert.True(IsInstalling(values, hasExecutables: false));
        Assert.False(IsInstalling(values, hasExecutables: true));
    }

    [Fact]
    public void InstallingGameIsDetectedWithoutRequiringExecutableReview()
    {
        var values = Manifest("1026");

        var detection = SteamGameDiscovery.CreateDetection(values, @"C:\Steam\steamapps", _ => []);

        Assert.NotNull(detection);
        Assert.Equal(InstallState.Installing, detection!.InstallState);
        Assert.False(detection.RequiresExecutableReview);
        Assert.Empty(detection.Processes);
    }

    [Fact]
    public void InstalledGameWithoutExecutablesStillRequiresExecutableReview()
    {
        var values = Manifest("4");

        var detection = SteamGameDiscovery.CreateDetection(values, @"C:\Steam\steamapps", _ => []);

        Assert.NotNull(detection);
        Assert.Equal(InstallState.Installed, detection!.InstallState);
        Assert.True(detection.RequiresExecutableReview);
    }

    [Fact]
    public void ManifestFilterReadsEveryAppWhenNoAppIdsAreRequested()
    {
        Assert.True(SteamGameDiscovery.ShouldReadManifest(@"C:\Steam\steamapps\appmanifest_123.acf", null));
    }

    [Fact]
    public void ManifestFilterReadsOnlyTheRequestedApps()
    {
        var appIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "123" };

        Assert.True(SteamGameDiscovery.ShouldReadManifest(@"C:\Steam\steamapps\appmanifest_123.acf", appIds));
        Assert.False(SteamGameDiscovery.ShouldReadManifest(@"C:\Steam\steamapps\appmanifest_456.acf", appIds));
        Assert.False(SteamGameDiscovery.ShouldReadManifest(@"C:\Steam\steamapps\unexpected.acf", appIds));
    }

    private static bool IsInstalling(IReadOnlyDictionary<string, string> values, bool hasExecutables) =>
        SteamGameDiscovery.ResolveInstallState(values, hasExecutables) == InstallState.Installing;

    private static Dictionary<string, string> Manifest(string? stateFlags)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["appid"] = "123",
            ["name"] = "Everwind",
            ["installdir"] = "Everwind"
        };
        if (stateFlags is not null)
        {
            values["StateFlags"] = stateFlags;
        }

        return values;
    }

    private static IEnumerable<string> ThrowDuringEnumeration()
    {
        yield return @"D:\\SteamLibrary\\steamapps\\appmanifest_123.acf";
        throw new UnauthorizedAccessException("Library became inaccessible.");
    }
}
