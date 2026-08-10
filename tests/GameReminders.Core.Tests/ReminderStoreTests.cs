using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class ReminderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"game-reminders-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CompleteMovesReminderFromInboxToCompleted()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var reminder = CreateReminder();
        var source = Path.Combine(store.InboxPath, $"{reminder.Id}.json");
        File.WriteAllText(source, JsonProtocol.WriteReminder(reminder));

        var pending = Assert.Single(store.LoadPending(reminder.GameId));
        store.Complete(pending);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(store.CompletedPath, Path.GetFileName(source))));
    }

    [Fact]
    public void CompleteRemovesDuplicateInboxFileWhenReminderIsAlreadyArchived()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var reminder = CreateReminder();
        var filename = $"{reminder.Id}.json";
        var source = Path.Combine(store.InboxPath, filename);
        var destination = Path.Combine(store.CompletedPath, filename);
        var json = JsonProtocol.WriteReminder(reminder);
        File.WriteAllText(source, json);
        File.WriteAllText(destination, json);

        var pending = Assert.Single(store.LoadPending(reminder.GameId));
        store.Complete(pending);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void LoadPendingFiltersByGameIdAndSortsByCreationTime()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var later = CreateReminder(createdAt: DateTimeOffset.Parse("2026-08-10T21:00:00Z"));
        var earlier = CreateReminder(createdAt: DateTimeOffset.Parse("2026-08-10T20:00:00Z"));
        var otherGame = CreateReminder(gameId: "another-game");

        Write(store, later);
        Write(store, earlier);
        Write(store, otherGame);

        var pending = store.LoadPending("custom-farever");

        Assert.Equal(new[] { earlier.Id, later.Id }, pending.Select(reminder => reminder.Id));
    }

    private static Reminder CreateReminder(
        string gameId = "custom-farever",
        DateTimeOffset? createdAt = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            GameNameAtCreation = "Farever",
            Message = "Change my build",
            CreatedAt = createdAt ?? DateTimeOffset.Parse("2026-08-10T20:35:00Z")
        };

    private static void Write(ReminderStore store, Reminder reminder) =>
        File.WriteAllText(Path.Combine(store.InboxPath, $"{reminder.Id}.json"), JsonProtocol.WriteReminder(reminder));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
