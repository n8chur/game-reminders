using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class NewReminderWindowTests
{
    private static readonly GameDefinition Game = new() { Id = "test", Name = "Test" };

    [Theory]
    [InlineData(null, "message", false)]
    [InlineData("game", "", false)]
    [InlineData("game", "   ", false)]
    [InlineData("game", "message", true)]
    public void CreationRequiresGameAndMessage(string? game, string message, bool expected)
    {
        Assert.Equal(expected, NewReminderWindow.CanCreate(game is null ? null : Game, message));
    }
}
