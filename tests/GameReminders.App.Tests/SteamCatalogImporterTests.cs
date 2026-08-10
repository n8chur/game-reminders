using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class SteamCatalogImporterTests
{
    [Fact]
    public void TrustedSteamGamesAreAddedWithStableIds()
    {
        var detection = SteamDetection("123", "Test Game", "TestGame.exe");

        var result = SteamCatalogImporter.Import(new GameCatalog(), [detection]);

        var game = Assert.Single(result.AddedGames);
        Assert.Equal("steam-123", game.Id);
        Assert.Equal("123", game.Source?.AppId);
        Assert.Equal("TestGame.exe", Assert.Single(game.Processes));
        Assert.Single(result.Catalog.Games);
    }

    [Fact]
    public void ImportSkipsExistingAppIdsAndRemovesConflictingProcesses()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition
                {
                    Id = "custom-existing",
                    Name = "Existing",
                    Processes = ["Existing.exe"],
                    Source = new GameSource { Type = "steam", AppId = "123" }
                }
            ]
        };

        var result = SteamCatalogImporter.Import(catalog,
        [
            SteamDetection("123", "Same app", "Other.exe"),
            SteamDetection("456", "Same process", "existing.EXE")
        ]);

        var added = Assert.Single(result.AddedGames);
        Assert.Equal("Same process", added.Name);
        Assert.Empty(added.Processes);
        Assert.Equal(2, result.Catalog.Games.Count);
    }

    [Fact]
    public void BatchImportKeepsBothGamesWithoutDuplicateProcessMappings()
    {
        var result = SteamCatalogImporter.Import(new GameCatalog(),
        [
            SteamDetection("123", "First", "Shared.exe"),
            SteamDetection("456", "Second", "shared.EXE")
        ]);

        Assert.Equal(2, result.AddedGames.Count);
        Assert.Equal("Shared.exe", Assert.Single(result.AddedGames[0].Processes));
        Assert.Empty(result.AddedGames[1].Processes);
    }

    private static PendingGameDetection SteamDetection(string appId, string name, string process) => new()
    {
        Key = $"steam:{appId}",
        Name = name,
        Processes = [process],
        SourceType = "steam",
        AppId = appId
    };
}
