namespace GameReminders.App.Tests;

public sealed class MainWindowTests
{
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
}
