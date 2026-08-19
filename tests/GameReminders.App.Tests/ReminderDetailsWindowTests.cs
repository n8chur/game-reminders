using GameReminders.Core;
using System.Text.Json;
using System.Xml.Linq;

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
    public void ViewModeHasDedicatedNonInteractiveGameDisplay()
    {
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ReminderDetailsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var picker = Assert.Single(xaml.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "GamePicker");
        var surface = Assert.Single(xaml.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "GameDisplaySurface");
        var displayText = Assert.Single(xaml.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "GameDisplayText");

        Assert.Equal("ComboBox", picker.Name.LocalName);
        Assert.Equal("Border", surface.Name.LocalName);
        Assert.Equal("Collapsed", (string?)surface.Attribute("Visibility"));
        Assert.Equal("TextBlock", displayText.Name.LocalName);
    }

    [Fact]
    public void PopupKeepsMessageEditsButRemovesGameReassignments()
    {
        var original = Reminder("game", "Game");

        Assert.True(ReminderWindow.RemainsInPopup(original, original with { Message = "Updated" }));
        Assert.False(ReminderWindow.RemainsInPopup(original, original with { GameId = "other" }));
    }

    [Theory]
    [MemberData(nameof(ReminderUpdateFailures))]
    public void ReminderUpdateFailuresAreHandled(Exception exception, bool expected)
    {
        Assert.Equal(expected, App.IsReminderUpdateFailure(exception));
    }

    public static TheoryData<Exception, bool> ReminderUpdateFailures => new()
    {
        { new IOException(), true },
        { new UnauthorizedAccessException(), true },
        { new InvalidDataException(), true },
        { new InvalidOperationException(), true },
        { new ArgumentException(), true },
        { new JsonException(), true },
        { new NotSupportedException(), false }
    };

    private static Reminder Reminder(string gameId, string gameName) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        GameNameAtCreation = gameName,
        Message = "Message",
        CreatedAt = DateTimeOffset.Parse("2026-08-19T12:00:00Z")
    };
}
