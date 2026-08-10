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
}
