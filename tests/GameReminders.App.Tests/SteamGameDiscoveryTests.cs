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
            (_, _) => ThrowDuringEnumeration());

        Assert.Empty(manifests);
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

    private static IEnumerable<string> ThrowDuringEnumeration()
    {
        yield return @"D:\\SteamLibrary\\steamapps\\appmanifest_123.acf";
        throw new UnauthorizedAccessException("Library became inaccessible.");
    }
}
