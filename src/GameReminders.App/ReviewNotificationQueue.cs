namespace GameReminders.App;

internal sealed record ReviewNotification(int Count, bool TrustedSteamGames);

internal sealed class ReviewNotificationQueue
{
    private readonly Queue<ReviewNotification> _pending = new();

    public ReviewNotification? Active { get; private set; }

    public bool Enqueue(int count, bool trustedSteamGames)
    {
        _pending.Enqueue(new ReviewNotification(count, trustedSteamGames));
        if (Active is not null)
        {
            return false;
        }

        Active = _pending.Dequeue();
        return true;
    }

    public ReviewNotification? CompleteActive()
    {
        var completed = Active;
        Active = _pending.Count > 0 ? _pending.Dequeue() : null;
        return completed;
    }
}
