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
}
