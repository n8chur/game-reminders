namespace GameReminders.App.Tests;

public sealed class MainWindowTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("registry unavailable", false)]
    public void LaunchAtLoginCanOnlyChangeWhenStatusIsKnown(string? statusError, bool expected)
    {
        Assert.Equal(expected, MainWindow.CanChangeLaunchAtLogin(statusError));
    }

    [Fact]
    public void CloseIsHiddenUntilApplicationExitIsAllowed()
    {
        Assert.True(MainWindow.ShouldHideOnClose(allowClose: false));
        Assert.False(MainWindow.ShouldHideOnClose(allowClose: true));
    }

    [Fact]
    public void SelectingUnreviewedRowAcknowledgesItEvenWhenActionBadgeTakesPriority()
    {
        var item = new GameListItem(
            new GameReminders.Core.GameDefinition { Id = "steam-123", Name = "Test" },
            IsUnreviewed: true,
            Badge: "ACTION REQUIRED");

        Assert.True(MainWindow.ShouldAcknowledge(
            item, isSelected: true, isVisible: true, acknowledgeVisibleRows: false));
    }

    [Fact]
    public void RefreshedItemsRestoreSelectionByStableGameId()
    {
        var selected = new GameListItem(
            new GameReminders.Core.GameDefinition { Id = "steam-123", Name = "Updated" },
            IsUnreviewed: false,
            Badge: string.Empty);
        var other = new GameListItem(
            new GameReminders.Core.GameDefinition { Id = "steam-456", Name = "Other" },
            IsUnreviewed: false,
            Badge: string.Empty);

        Assert.Same(selected, MainWindow.FindItemByGameId([other, selected], "STEAM-123"));
        Assert.Null(MainWindow.FindItemByGameId([other, selected], null));
    }

    [Fact]
    public void DeactivationAcknowledgesOnlyVisibleUnreviewedRows()
    {
        var item = new GameListItem(
            new GameReminders.Core.GameDefinition { Id = "steam-123", Name = "Test" },
            IsUnreviewed: true,
            Badge: "NEW");

        Assert.True(MainWindow.ShouldAcknowledge(
            item, isSelected: false, isVisible: true, acknowledgeVisibleRows: true));
        Assert.False(MainWindow.ShouldAcknowledge(
            item, isSelected: false, isVisible: false, acknowledgeVisibleRows: true));
    }

    [Theory]
    [InlineData(0, 0, 100, 20, true)]
    [InlineData(0, -1, 100, 20, false)]
    [InlineData(0, 81, 100, 20, false)]
    public void VisibleRowMustBeFullyInsideViewport(
        double x,
        double y,
        double width,
        double height,
        bool expected)
    {
        var itemBounds = new System.Windows.Rect(x, y, width, height);
        var viewport = new System.Windows.Rect(0, 0, 100, 100);

        Assert.Equal(expected, MainWindow.IsFullyWithinViewport(itemBounds, viewport));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("selected", true)]
    public void RowActionsRequireSelection(object? selectedItem, bool expected)
    {
        Assert.Equal(expected, MainWindow.HasSelection(selectedItem));
    }

    [Theory]
    [InlineData("Farever", "fare", true)]
    [InlineData("Farever", "EVER", true)]
    [InlineData("Farever", "monster", false)]
    [InlineData("Farever", "", true)]
    public void SearchMatchesGameNamesCaseInsensitively(string value, string query, bool expected)
    {
        Assert.Equal(expected, MainWindow.Matches(value, query));
    }

    [Theory]
    [InlineData(0, "game", "0 games")]
    [InlineData(1, "game", "1 game")]
    [InlineData(2, "item", "2 items")]
    public void CountLabelsUseCorrectPlural(int count, string singular, string expected)
    {
        Assert.Equal(expected, MainWindow.CountLabel(count, singular));
    }

    [Fact]
    public void BadgeColorsDistinguishActionFromNew()
    {
        var game = new GameReminders.Core.GameDefinition { Id = "test", Name = "Test" };

        Assert.Equal("#B42318", new GameListItem(game, true, "ACTION REQUIRED").BadgeBackground);
        Assert.Equal("#16803A", new GameListItem(game, true, "NEW").BadgeBackground);
    }

    [Fact]
    public void ReminderDetailsDoNotRepeatTheGroupGameName()
    {
        var item = new ReminderListItem(
            new GameReminders.Core.Reminder
            {
                Id = Guid.Parse("3f0648ac-0d2c-4a68-bc05-f9760ed663e7"),
                GameId = "test-game",
                GameNameAtCreation = "Farever",
                Message = "Test",
                CreatedAt = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero)
            },
            "Farever");

        Assert.DoesNotContain("Farever", item.Details);
    }
}
