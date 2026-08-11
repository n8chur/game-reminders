using GameReminders.Core;

namespace GameReminders.App;

internal sealed class ReminderSessionState
{
    private readonly HashSet<Guid> _deferredIds = [];

    public void Defer(Reminder reminder) => _deferredIds.Add(reminder.Id);

    public void Complete(Reminder reminder) => _deferredIds.Remove(reminder.Id);

    public void BeginNextLaunch(IEnumerable<Reminder> reminders)
    {
        foreach (var reminder in reminders)
        {
            _deferredIds.Remove(reminder.Id);
        }
    }

    public ReminderLists Partition(
        IEnumerable<Reminder> pending,
        IEnumerable<Reminder> completed,
        IReadOnlyDictionary<string, string> catalogNames)
    {
        var pendingItems = pending
            .OrderBy(reminder => reminder.CreatedAt)
            .Select(reminder => App.ToListItem(reminder, catalogNames))
            .ToArray();
        return new ReminderLists(
            pendingItems.Where(item => !_deferredIds.Contains(item.Reminder.Id)).ToArray(),
            pendingItems.Where(item => _deferredIds.Contains(item.Reminder.Id)).ToArray(),
            completed.OrderByDescending(reminder => reminder.CreatedAt)
                .Select(reminder => App.ToListItem(reminder, catalogNames))
                .ToArray());
    }
}

internal sealed record ReminderLists(
    IReadOnlyList<ReminderListItem> Pending,
    IReadOnlyList<ReminderListItem> NextLaunch,
    IReadOnlyList<ReminderListItem> Completed);
