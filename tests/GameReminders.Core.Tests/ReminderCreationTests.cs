using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class ReminderCreationTests
{
    private static readonly Guid ReminderId = Guid.Parse("0198a7de-81a2-74fe-b560-0242ac120002");
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-08-10T20:35:00Z");

    [Fact]
    public void SelectedStableGameIdCreatesReminder()
    {
        var result = ReminderCreation.Create(Catalog(), "CUSTOM-FAREVER", "Change my build", ReminderId, CreatedAt);

        var reminder = Assert.IsType<Reminder>(result.Reminder);
        Assert.Equal(ReminderCreationStatus.Created, result.Status);
        Assert.Equal("custom-farever", reminder.GameId);
        Assert.Equal("Farever", reminder.GameNameAtCreation);
        Assert.Equal("Change my build", reminder.Message);
        Assert.Equal(ReminderId, reminder.Id);
        Assert.Equal(CreatedAt, reminder.CreatedAt);
    }

    [Theory]
    [InlineData("", ReminderCreationStatus.EmptyGameId)]
    [InlineData("   ", ReminderCreationStatus.EmptyGameId)]
    [InlineData("Farever", ReminderCreationStatus.GameNotFound)]
    [InlineData("missing-id", ReminderCreationStatus.GameNotFound)]
    public void InvalidStableGameIdCreatesNoReminder(string gameId, ReminderCreationStatus expected)
    {
        var result = ReminderCreation.Create(Catalog(), gameId, "Change my build", ReminderId, CreatedAt);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Reminder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMessageCreatesNoReminder(string message)
    {
        var result = ReminderCreation.Create(Catalog(), "custom-farever", message, ReminderId, CreatedAt);

        Assert.Equal(ReminderCreationStatus.EmptyMessage, result.Status);
        Assert.Null(result.Reminder);
    }

    [Fact]
    public void CreatedReminderRoundTripsThroughProtocol()
    {
        var result = ReminderCreation.Create(Catalog(), "custom-farever", "Change my build", ReminderId, CreatedAt);
        var reminder = Assert.IsType<Reminder>(result.Reminder);

        var roundTrip = JsonProtocol.ReadReminder(JsonProtocol.WriteReminder(reminder));

        Assert.Equal(reminder, roundTrip);
    }

    private static GameCatalog Catalog() => new()
    {
        Games = [new GameDefinition { Id = "custom-farever", Name = "Farever" }]
    };
}
