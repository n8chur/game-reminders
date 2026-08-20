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
        Assert.Equal("#1F6FEB", new GameListItem(game, true, "INSTALLING").BadgeBackground);
        Assert.Equal("#57606A", new GameListItem(game, true, "NOT INSTALLED").BadgeBackground);
        Assert.Equal("#16803A", new GameListItem(game, true, "NEW").BadgeBackground);
    }

    [Fact]
    public void InstallingBadgeOutranksExecutableReviewAndNew()
    {
        var game = new GameReminders.Core.GameDefinition
        {
            Id = "steam-123",
            Name = "Everwind",
            Source = new GameReminders.Core.GameSource
            {
                Type = "steam",
                AppId = "123",
                InstallState = GameReminders.Core.InstallState.Installing,
                RequiresExecutableReview = true
            }
        };

        Assert.Equal("INSTALLING", MainWindow.DescribeBadge(game, isUnreviewed: true));
    }

    [Fact]
    public void BadgePrecedenceFallsBackToReviewThenNewThenNothing()
    {
        var plain = new GameReminders.Core.GameDefinition { Id = "test", Name = "Test" };
        var review = plain with
        {
            Source = new GameReminders.Core.GameSource { Type = "steam", RequiresExecutableReview = true }
        };

        Assert.Equal("ACTION REQUIRED", MainWindow.DescribeBadge(review, isUnreviewed: true));
        Assert.Equal("NEW", MainWindow.DescribeBadge(plain, isUnreviewed: true));
        Assert.Equal(string.Empty, MainWindow.DescribeBadge(plain, isUnreviewed: false));
    }

    [Fact]
    public void NotInstalledBadgeOutranksExecutableReview()
    {
        var game = new GameReminders.Core.GameDefinition
        {
            Id = "steam-456",
            Name = "Other",
            Source = new GameReminders.Core.GameSource
            {
                Type = "steam",
                AppId = "456",
                InstallState = GameReminders.Core.InstallState.NotInstalled,
                RequiresExecutableReview = true
            }
        };

        Assert.Equal("NOT INSTALLED", MainWindow.DescribeBadge(game, isUnreviewed: true));
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0, "Game scan added 1 new game(s)")]
    [InlineData(0, 2, 0, 0, 0, "Game scan updated 2 existing game(s)")]
    [InlineData(0, 0, 0, 0, 0, "Game scan found no new games")]
    [InlineData(1, 0, 2, 0, 0, "Game scan added 1 new game(s); 2 still installing")]
    [InlineData(0, 0, 1, 0, 0, "Game scan found no new games; 1 still installing")]
    [InlineData(0, 1, 0, 0, 1, "Game scan updated 1 existing game(s); 1 no longer installed")]
    [InlineData(0, 0, 0, 2, 0, "Game scan found no new games; 2 cancelled install(s) removed")]
    [InlineData(0, 1, 1, 1, 1, "Game scan updated 1 existing game(s); 1 still installing, 1 no longer installed, 1 cancelled install(s) removed")]
    public void ScanResultReportsInstallStateChanges(
        int added, int updated, int installing, int retracted, int uninstalled, string expected)
    {
        Assert.Equal(expected, App.DescribeScanResult(added, updated, installing, retracted, uninstalled));
    }

    [Fact]
    public void HideUninstalledHidesOnlyUninstalledGamesAndComposesWithSearch()
    {
        var installed = Item("Alpha", GameReminders.Core.InstallState.Installed);
        var installing = Item("Alpha Two", GameReminders.Core.InstallState.Installing);
        var uninstalled = Item("Alpha Three", GameReminders.Core.InstallState.NotInstalled);

        Assert.True(MainWindow.IsVisibleGame(installed, string.Empty, hideUninstalled: true));
        Assert.True(MainWindow.IsVisibleGame(installing, string.Empty, hideUninstalled: true));
        Assert.False(MainWindow.IsVisibleGame(uninstalled, string.Empty, hideUninstalled: true));
        Assert.True(MainWindow.IsVisibleGame(uninstalled, string.Empty, hideUninstalled: false));
        Assert.False(MainWindow.IsVisibleGame(installed, "zzz", hideUninstalled: false));
    }

    [Fact]
    public void EmptyGamesTextExplainsWhyTheListIsEmpty()
    {
        Assert.Equal("No games yet. Add one manually or choose Scan games.",
            MainWindow.DescribeEmptyGames(0, string.Empty, hideUninstalled: false));
        Assert.Equal("No games match this search.",
            MainWindow.DescribeEmptyGames(3, "zzz", hideUninstalled: true));
        Assert.Equal("Every game is hidden because it is not installed. Untick Hide uninstalled to see them.",
            MainWindow.DescribeEmptyGames(3, string.Empty, hideUninstalled: true));
        Assert.Equal("No games match this search.",
            MainWindow.DescribeEmptyGames(3, string.Empty, hideUninstalled: false));
    }

    private static GameListItem Item(string name, GameReminders.Core.InstallState state) => new(
        new GameReminders.Core.GameDefinition
        {
            Id = name,
            Name = name,
            Source = new GameReminders.Core.GameSource { Type = "steam", AppId = name, InstallState = state }
        },
        false,
        string.Empty);

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

    [Theory]
    [InlineData("First\nSecond\nThird", "First\nSecond\nThird")]
    [InlineData("First\nSecond\nThird\nFourth", "First\nSecond\nThird…")]
    [InlineData("First\r\nSecond\r\nThird\r\nFourth", "First\nSecond\nThird…")]
    [InlineData("First\rSecond\rThird\rFourth", "First\nSecond\nThird…")]
    public void ReminderPreviewShowsThreeLinesAndEllipsisWhenMoreRemain(
        string message,
        string expected)
    {
        Assert.Equal(
            expected.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            ReminderListItem.CreatePreview(message, 3));
    }

    [Fact]
    public void GameManagementContainsNoObsoleteRequestSection()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var xaml = XDocument.Load(xamlPath);

        Assert.DoesNotContain("ali" + "as", xaml.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReminderListsUseCompactThreeLineEllipsisPreviews()
    {
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var previews = xaml.Descendants()
            .Where(element =>
                element.Name.LocalName == "TextBlock" &&
                (string?)element.Attribute("Text") == "{Binding PreviewMessage}")
            .ToArray();

        Assert.Equal(2, previews.Length);
        Assert.All(previews, preview =>
        {
            Assert.Equal("13", (string?)preview.Attribute("FontSize"));
            Assert.Equal("Wrap", (string?)preview.Attribute("TextWrapping"));
            Assert.Equal("17", (string?)preview.Attribute("LineHeight"));
            Assert.Equal("51", (string?)preview.Attribute("MaxHeight"));
            Assert.Equal("CharacterEllipsis", (string?)preview.Attribute("TextTrimming"));
        });
    }

    [Fact]
    public void ReminderListsConstrainRowsToTheViewportForTextWrapping()
    {
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = Assert.Single(xaml.Descendants(), element =>
            element.Name.LocalName == "Style" &&
            (string?)element.Attribute(x + "Key") == "ReminderListBoxStyle");
        var setters = style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                element => (string)element.Attribute("Property")!,
                element => (string)element.Attribute("Value")!);

        Assert.Equal("Stretch", setters["HorizontalContentAlignment"]);
        Assert.Equal("Disabled", setters["ScrollViewer.HorizontalScrollBarVisibility"]);
    }

    [Fact]
    public void ReminderListsExposeDoubleClickAndContextDetailsActions()
    {
        var xaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var lists = xaml.Descendants()
            .Where(element => (string?)element.Attribute("MouseDoubleClick") == "ReminderList_MouseDoubleClick")
            .ToArray();

        Assert.Equal(2, lists.Length);
        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Header") == "Edit" &&
            (string?)element.Attribute("Click") == "EditSelectedReminder_Click");
        Assert.Contains(xaml.Descendants(), element =>
            (string?)element.Attribute("Header") == "View" &&
            (string?)element.Attribute("Click") == "ViewSelectedReminder_Click");
    }
}
