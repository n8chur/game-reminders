using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class ReminderCreationTests
{
    private static readonly Guid ReminderId = Guid.Parse("0198a7de-81a2-74fe-b560-0242ac120002");
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-08-10T20:35:00Z");

    [Theory]
    [InlineData("Farever")]
    [InlineData("FOREVER!")]
    [InlineData("for ever")]
    public void CanonicalNameAndAliasesResolveAfterNormalization(string requestedName)
    {
        var result = ReminderCreation.Create(Catalog(), requestedName, "Change my build", ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.Created, result.Status);
        Assert.Equal("custom-farever", result.Reminder?.GameId);
    }

    [Fact]
    public void DuplicateMatchingNamesWithinOneGameStillResolveOnce()
    {
        var catalog = Catalog() with
        {
            Games = [Catalog().Games[0] with { Aliases = ["Farever", "For Ever"] }]
        };

        var result = ReminderCreation.Create(catalog, "Farever", "Change my build", ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.Created, result.Status);
    }

    [Theory]
    [InlineData("Missing", ReminderCreationStatus.UnknownGame)]
    [InlineData(" ! ", ReminderCreationStatus.EmptyGameName)]
    public void InvalidGameInputCreatesNoReminder(string requestedName, ReminderCreationStatus expected)
    {
        var result = ReminderCreation.Create(Catalog(), requestedName, "Change my build", ReminderId, CreatedAt);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Reminder);
    }

    [Fact]
    public void NormalizedCollisionIsAmbiguousAndCreatesNoReminder()
    {
        var catalog = Catalog() with
        {
            Games =
            [
                Catalog().Games[0],
                new GameDefinition { Id = "other", Name = "For-Ever" }
            ]
        };

        var result = ReminderCreation.Create(catalog, "forever", "Change my build", ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.AmbiguousGame, result.Status);
        Assert.Null(result.Reminder);
    }

    [Fact]
    public void DiacriticsArePreservedToMatchShortcutNormalization()
    {
        var catalog = Catalog() with
        {
            Games = [new GameDefinition { Id = "custom-pokemon", Name = "Pokémon" }]
        };

        var withoutDiacritic = ReminderCreation.Create(catalog, "Pokemon", "Check my team", ReminderId, CreatedAt);
        var withDiacritic = ReminderCreation.Create(catalog, "Pokémon", "Check my team", ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.UnknownGame, withoutDiacritic.Status);
        Assert.Null(withoutDiacritic.Reminder);
        Assert.Equal(ReminderCreationStatus.Created, withDiacritic.Status);
        Assert.Equal("custom-pokemon", withDiacritic.Reminder?.GameId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMessageCreatesNoReminder(string message)
    {
        var result = ReminderCreation.Create(Catalog(), "Farever", message, ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.EmptyMessage, result.Status);
        Assert.Null(result.Reminder);
    }

    [Fact]
    public void CreatedReminderUsesStableGameIdentityAndProtocolFields()
    {
        var result = ReminderCreation.Create(Catalog(), "Forever", "Change my build", ReminderId, CreatedAt);

        var reminder = Assert.IsType<Reminder>(result.Reminder);
        Assert.Equal(1, reminder.SchemaVersion);
        Assert.Equal(ReminderId, reminder.Id);
        Assert.Equal("custom-farever", reminder.GameId);
        Assert.Equal("Farever", reminder.GameNameAtCreation);
        Assert.Equal("Change my build", reminder.Message);
        Assert.Equal(CreatedAt, reminder.CreatedAt);
        Assert.Null(reminder.SourcePath);

        var roundTrip = JsonProtocol.ReadReminder(JsonProtocol.WriteReminder(reminder));
        Assert.Equal(reminder, roundTrip);
    }

    private static GameCatalog Catalog() => new()
    {
        Games =
        [
            new GameDefinition
            {
                Id = "custom-farever",
                Name = "Farever",
                Aliases = ["Forever", "For Ever"]
            }
        ]
    };
}
