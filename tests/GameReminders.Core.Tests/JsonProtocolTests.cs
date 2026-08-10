using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class JsonProtocolTests
{
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
}
