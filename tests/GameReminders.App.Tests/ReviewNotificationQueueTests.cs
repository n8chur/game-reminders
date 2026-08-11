namespace GameReminders.App.Tests;

public sealed class ReviewNotificationQueueTests
{
    [Fact]
    public void OverlappingNotificationsAreDisplayedInOrder()
    {
        var queue = new ReviewNotificationQueue();

        Assert.True(queue.Enqueue(2, trustedSteamGames: true));
        Assert.False(queue.Enqueue(1, trustedSteamGames: false));
        Assert.True(queue.Active!.TrustedSteamGames);

        var first = queue.CompleteActive();

        Assert.True(first!.TrustedSteamGames);
        Assert.False(queue.Active!.TrustedSteamGames);
        Assert.Equal(1, queue.Active.Count);
    }

    [Fact]
    public void ReadingClickedNotificationDoesNotAdvanceQueueBeforeClose()
    {
        var queue = new ReviewNotificationQueue();
        queue.Enqueue(2, trustedSteamGames: true);
        queue.Enqueue(1, trustedSteamGames: false);

        var clicked = queue.Active;

        Assert.True(clicked!.TrustedSteamGames);
        Assert.Same(clicked, queue.Active);

        var closed = queue.CompleteActive();

        Assert.Same(clicked, closed);
        Assert.False(queue.Active!.TrustedSteamGames);
        Assert.Equal(1, queue.Active.Count);
    }

    [Fact]
    public void ClosingNotificationSchedulesNextDisplayThroughDispatcher()
    {
        var queue = new ReviewNotificationQueue();
        queue.Enqueue(2, trustedSteamGames: true);
        queue.Enqueue(1, trustedSteamGames: false);
        Action? scheduled = null;
        var displays = 0;

        App.DispatchAfterReviewNotificationClosed(
            queue,
            action => scheduled = action,
            () => displays++);

        Assert.NotNull(scheduled);
        Assert.False(queue.Active!.TrustedSteamGames);
        Assert.Equal(0, displays);

        scheduled!();

        Assert.Equal(1, displays);
    }
}
