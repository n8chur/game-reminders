using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class JsonProtocolTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("{}")]
    [InlineData("{ } ")]
    public void EmptyCatalogPlaceholderIsTreatedAsNewCatalog(string json)
    {
        var catalog = JsonProtocol.ReadCatalog(json);

        Assert.Empty(catalog.Games);
        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(DateTimeOffset.UnixEpoch, catalog.UpdatedAt);
    }

    [Fact]
    public void CatalogRoundTripPreservesGameIdentityAndProcesses()
    {
        var catalog = new GameCatalog
        {
            UpdatedAt = DateTimeOffset.Parse("2026-08-10T20:30:00Z"),
            Games =
            [
                new GameDefinition
                {
                    Id = "custom-farever",
                    Name = "Farever",
                    Aliases = ["Forever"],
                    Processes = ["Farever-Win64-Shipping.exe"]
                }
            ]
        };

        var result = JsonProtocol.ReadCatalog(JsonProtocol.WriteCatalog(catalog));

        Assert.Equal("custom-farever", Assert.Single(result.Games).Id);
        Assert.Equal("Farever-Win64-Shipping.exe", Assert.Single(result.Games[0].Processes));
    }

    [Fact]
    public void UnsupportedReminderSchemaIsRejected()
    {
        const string json = """
            {
              "schemaVersion": 99,
              "id": "0198a7de-81a2-74fe-b560-0242ac120002",
              "gameId": "custom-farever",
              "gameNameAtCreation": "Farever",
              "message": "Change my build",
              "createdAt": "2026-08-10T20:35:00Z"
            }
            """;

        Assert.Throws<InvalidDataException>(() => JsonProtocol.ReadReminder(json));
    }

    [Fact]
    public void EmptyGameNameAtCreationIsRejected()
    {
        var reminder = CreateReminder() with { GameNameAtCreation = " " };

        Assert.Throws<InvalidDataException>(() => JsonProtocol.WriteReminder(reminder));
    }

    [Fact]
    public void DefaultCreatedAtIsRejected()
    {
        var reminder = CreateReminder() with { CreatedAt = default };

        Assert.Throws<InvalidDataException>(() => JsonProtocol.WriteReminder(reminder));
    }

    private static Reminder CreateReminder() => new()
    {
        Id = Guid.Parse("0198a7de-81a2-74fe-b560-0242ac120002"),
        GameId = "custom-farever",
        GameNameAtCreation = "Farever",
        Message = "Change my build",
        CreatedAt = DateTimeOffset.Parse("2026-08-10T20:35:00Z")
    };

    [Fact]
    public void ProcessAssignedToMultipleGamesIsRejected()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition { Id = "first", Name = "First", Processes = ["Shared.exe"] },
                new GameDefinition { Id = "second", Name = "Second", Processes = ["SHARED"] }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(() => JsonProtocol.WriteCatalog(catalog));

        Assert.Contains("assigned to both", exception.Message);
    }

    [Fact]
    public void AbsoluteAndRelativeMappingsForSameExecutableAreRejected()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition { Id = "steam", Name = "Steam", Processes = [@"Everwind\Everwind.exe"] },
                new GameDefinition
                {
                    Id = "manual",
                    Name = "Manual",
                    Processes = [@"D:\SteamLibrary\steamapps\common\Everwind\Everwind.exe"]
                }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(() => JsonProtocol.WriteCatalog(catalog));

        Assert.Contains("assigned to both", exception.Message);
    }

    [Fact]
    public void SameFilenameInDifferentGamePathsIsAllowed()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition { Id = "first", Name = "First", Processes = [@"First\Binaries\Game.exe"] },
                new GameDefinition { Id = "second", Name = "Second", Processes = [@"Second\Binaries\Game.exe"] }
            ]
        };

        JsonProtocol.WriteCatalog(catalog);
    }

    [Fact]
    public void NullProcessNameIsRejectedAsInvalidCatalogData()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "games": [
                {
                  "id": "custom-farever",
                  "name": "Farever",
                  "processes": [null]
                }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() => JsonProtocol.ReadCatalog(json));

        Assert.Contains("empty process name", exception.Message);
    }

    [Fact]
    public void ProcessNameThatNormalizesToEmptyIsRejected()
    {
        var catalog = new GameCatalog
        {
            Games = [new GameDefinition { Id = "game", Name = "Game", Processes = [".exe"] }]
        };

        var exception = Assert.Throws<InvalidDataException>(() => JsonProtocol.WriteCatalog(catalog));

        Assert.Contains("empty process name", exception.Message);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, \"games\": null }")]
    [InlineData("{ \"schemaVersion\": 1, \"games\": [null] }")]
    [InlineData("{ \"schemaVersion\": 1, \"games\": [{ \"id\": \"game\", \"name\": \"Game\", \"processes\": null }] }")]
    public void NullCatalogCollectionsAndEntriesAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => JsonProtocol.ReadCatalog(json));
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, \"games\": [{ \"id\": \"game\", \"name\": \"Game\", \"aliases\": null }] }")]
    [InlineData("{ \"schemaVersion\": 1, \"games\": [{ \"id\": \"game\", \"name\": \"Game\", \"aliases\": [null] }] }")]
    [InlineData("{ \"schemaVersion\": 1, \"games\": [{ \"id\": \"game\", \"name\": \"Game\", \"aliases\": [\"  \" ] }] }")]
    public void NullOrEmptyAliasesAreRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => JsonProtocol.ReadCatalog(json));
    }
}
