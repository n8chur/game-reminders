using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class ReminderDetailsWindowTests
{
    [Theory]
    [InlineData(true, true, "message", true)]
    [InlineData(false, true, "message", false)]
    [InlineData(true, false, "message", false)]
    [InlineData(true, true, "   ", false)]
    public void SavingRequiresEditableModeGameAndMessage(
        bool editable,
        bool hasGame,
        string message,
        bool expected)
    {
        var game = hasGame ? new GameDefinition { Id = "game", Name = "Game" } : null;

        Assert.Equal(expected, ReminderDetailsWindow.CanSave(editable, game, message));
    }

    [Fact]
    public void RemovedCurrentGameRemainsAvailableAsUnavailableOption()
    {
        var reminder = Reminder("removed", "Removed Game");
        var configured = new GameDefinition { Id = "configured", Name = "Configured Game" };

        var options = ReminderDetailsWindow.BuildGameOptions(reminder, [configured]);

        var current = Assert.Single(options, option => option.Game.Id == "removed");
        Assert.True(current.IsUnavailable);
        Assert.Equal("Removed Game (not configured)", current.DisplayName);
        Assert.Contains(options, option => option.Game == configured && !option.IsUnavailable);
    }

    [Fact]
    public void ConfiguredCurrentGameDoesNotCreateUnavailableDuplicate()
    {
        var reminder = Reminder("game", "Old Name");
        var configured = new GameDefinition { Id = "GAME", Name = "Current Name" };

        var option = Assert.Single(ReminderDetailsWindow.BuildGameOptions(reminder, [configured]));

        Assert.False(option.IsUnavailable);
        Assert.Equal("Current Name", option.DisplayName);
    }

    [Fact]
    public void PopupKeepsMessageEditsButRemovesGameReassignments()
    {
        var original = Reminder("game", "Game");

        Assert.True(ReminderWindow.RemainsInPopup(original, original with { Message = "Updated" }));
        Assert.False(ReminderWindow.RemainsInPopup(original, original with { GameId = "other" }));
    }

    private static Reminder Reminder(string gameId, string gameName) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        GameNameAtCreation = gameName,
        Message = "Message",
        CreatedAt = DateTimeOffset.Parse("2026-08-19T12:00:00Z")
    };
}
