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

    private static IEnumerable<string> ThrowDuringEnumeration()
    {
        yield return @"D:\\SteamLibrary\\steamapps\\appmanifest_123.acf";
        throw new UnauthorizedAccessException("Library became inaccessible.");
    }
}
