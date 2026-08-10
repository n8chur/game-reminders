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

    private static IEnumerable<string> ThrowDuringEnumeration()
    {
        yield return @"D:\\SteamLibrary\\steamapps\\appmanifest_123.acf";
        throw new UnauthorizedAccessException("Library became inaccessible.");
    }
}
