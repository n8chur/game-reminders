using System.Xml.Linq;

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

    [Fact]
    public void AliasRequestRowsShowSubmittedAliasAndSelectedGame()
    {
        var item = new AliasRequestListItem(
            new GameReminders.Core.AliasRequest
            {
                Id = Guid.Parse("9f6db96e-1c50-4785-91d6-94580d2ab833"),
                GameId = "custom-farever",
                Alias = "Fare ever",
                CreatedAt = DateTimeOffset.Parse("2026-08-12T08:00:00Z")
            },
            "Farever",
            "The game no longer exists.");

        Assert.Equal("“Fare ever”", item.AliasLabel);
        Assert.Contains("Farever", item.Details);
        Assert.Contains("no longer exists", item.Details);
    }

    [Fact]
    public void FailedAliasRequestActionsAreEmbeddedInNonSelectableRows()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var xaml = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var requestList = xaml.Descendants(presentation + "ItemsControl")
            .Single(element => (string?)element.Attribute(x + "Name") == "AliasRequestsList");
        var rowButtons = requestList.Descendants(presentation + "Button").ToArray();

        Assert.Contains(rowButtons, button => (string?)button.Attribute("Content") == "Retry");
        Assert.Contains(rowButtons, button => (string?)button.Attribute("Content") == "Reject");
        Assert.All(rowButtons, button => Assert.Equal("{Binding Request}", (string?)button.Attribute("Tag")));
        Assert.DoesNotContain(requestList.DescendantsAndSelf(), element =>
            element.Name == presentation + "ListBox" ||
            element.Attribute("SelectionChanged") is not null);
    }

    [Fact]
    public void AliasRetryErrorsHaveALocalBanner()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var xaml = XDocument.Load(xamlPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var names = xaml.Descendants()
            .Select(element => (string?)element.Attribute(x + "Name"))
            .Where(name => name is not null)
            .ToHashSet();

        Assert.Contains("AliasRequestStatusBanner", names);
        Assert.Contains("AliasRequestStatusText", names);
    }
}
