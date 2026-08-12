using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class ReminderSessionStateTests
{
    [Fact]
    public void DeferredRemindersStayInPendingManagementList()
    {
        var state = new ReminderSessionState();
        var earlier = Reminder("configured", "Stored name", 1);
        var later = Reminder("missing", "Unknown Game", 2);
        state.Defer(later);

        var deferred = state.Partition([later, earlier], [], Names());

        Assert.Equal([earlier.Id, later.Id], deferred.Pending.Select(item => item.Reminder.Id));
        Assert.Equal("Current Name", deferred.Pending[0].GameName);
        Assert.Equal("Unknown Game", deferred.Pending[1].GameName);

        state.BeginNextLaunch([later]);
        var nextLaunch = state.Partition([later, earlier], [], Names());

        Assert.Equal([earlier.Id, later.Id], nextLaunch.Pending.Select(item => item.Reminder.Id));
    }

    [Fact]
    public void CompletedRemindersAreNewestFirstAndClearSessionDeferral()
    {
        var state = new ReminderSessionState();
        var earlier = Reminder("configured", "Stored name", 1);
        var later = Reminder("configured", "Stored name", 2);
        state.Defer(later);
        state.Complete(later);

        var lists = state.Partition([], [earlier, later], Names());

        Assert.Equal([later.Id, earlier.Id], lists.Completed.Select(item => item.Reminder.Id));
    }

    private static IReadOnlyDictionary<string, string> Names() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["configured"] = "Current Name" };

    private static Reminder Reminder(string gameId, string gameName, int hour) => new()
    {
        Id = Guid.NewGuid(),
        GameId = gameId,
        GameNameAtCreation = gameName,
        Message = "Remember this",
        CreatedAt = new DateTimeOffset(2026, 8, 11, hour, 0, 0, TimeSpan.Zero),
        SourcePath = $@"C:\inbox\{Guid.NewGuid():D}.json"
    };
}
