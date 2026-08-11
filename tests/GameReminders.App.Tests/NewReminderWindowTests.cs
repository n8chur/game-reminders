using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class NewReminderWindowTests
{
    private static readonly GameDefinition Game = new() { Id = "test", Name = "Test" };

    [Fact]
    public void DefaultGameUsesTheNewestReminderForAnAvailableGame()
    {
        var olderGame = new GameDefinition { Id = "older", Name = "Older" };
        var newerGame = new GameDefinition { Id = "newer", Name = "Newer" };
        var result = NewReminderWindow.ChooseDefaultGame(
            [olderGame, newerGame],
            [
                ReminderFor(olderGame, new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)),
                ReminderFor(newerGame, new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero))
            ]);

        Assert.Same(newerGame, result);
    }

    [Fact]
    public void DefaultGameFallsBackToTheFirstAvailableGameWhenNoReminderGameIsAvailable()
    {
        var firstGame = new GameDefinition { Id = "first", Name = "First" };
        var result = NewReminderWindow.ChooseDefaultGame(
            [firstGame],
            [ReminderFor(new GameDefinition { Id = "removed", Name = "Removed" }, DateTimeOffset.UtcNow)]);

        Assert.Same(firstGame, result);
    }

    [Theory]
    [InlineData(null, "message", false)]
    [InlineData("game", "", false)]
    [InlineData("game", "   ", false)]
    [InlineData("game", "message", true)]
    public void CreationRequiresGameAndMessage(string? game, string message, bool expected)
    {
        Assert.Equal(expected, NewReminderWindow.CanCreate(game is null ? null : Game, message));
    }

    private static Reminder ReminderFor(GameDefinition game, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        GameNameAtCreation = game.Name,
        Message = "Test reminder",
        CreatedAt = createdAt
    };
}
