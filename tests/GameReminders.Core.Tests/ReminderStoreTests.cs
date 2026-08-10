using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class ReminderStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"game-reminders-tests-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public void LoadCatalogRecreatesEmptyCatalogPlaceholder(string contents)
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        File.WriteAllText(store.CatalogPath, contents);

        var catalog = store.LoadCatalog();

        Assert.Empty(catalog.Games);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, catalog.UpdatedAt);
        var rewritten = File.ReadAllText(store.CatalogPath);
        Assert.Contains("\"schemaVersion\": 1", rewritten);
        Assert.Contains("\"games\": []", rewritten);
    }

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
    public void CompleteSucceedsWhenSyncAlreadyRemovedInboxFileAndArchivedReminderExists()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var reminder = CreateReminder();
        var filename = $"{reminder.Id}.json";
        var source = Path.Combine(store.InboxPath, filename);
        var destination = Path.Combine(store.CompletedPath, filename);
        File.WriteAllText(destination, JsonProtocol.WriteReminder(reminder));
        var pending = reminder with { SourcePath = source };

        store.Complete(pending);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void CompletePreservesConflictingInboxAndArchivedFilesWithTheSameId()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var reminder = CreateReminder();
        var conflictingReminder = reminder with { Message = "Conflicting archived message" };
        var filename = $"{reminder.Id}.json";
        var source = Path.Combine(store.InboxPath, filename);
        var destination = Path.Combine(store.CompletedPath, filename);
        var sourceJson = JsonProtocol.WriteReminder(reminder);
        var destinationJson = JsonProtocol.WriteReminder(conflictingReminder);
        File.WriteAllText(source, sourceJson);
        File.WriteAllText(destination, destinationJson);

        var pending = Assert.Single(store.LoadPending(reminder.GameId));

        var exception = Assert.Throws<InvalidDataException>(() => store.Complete(pending));

        Assert.Contains("conflicts", exception.Message);
        Assert.Equal(sourceJson, File.ReadAllText(source));
        Assert.Equal(destinationJson, File.ReadAllText(destination));
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

    [Fact]
    public void RepeatedInvalidReminderIsMovedToInvalidAndReported()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var source = Path.Combine(store.InboxPath, "malformed.json");
        File.WriteAllText(source, "{ not-json }");
        InvalidReminderEventArgs? detected = null;
        store.InvalidReminderDetected += (_, args) => detected = args;

        store.LoadPending("custom-farever");
        store.LoadPending("custom-farever");

        Assert.True(File.Exists(source));
        Assert.Null(detected);

        store.LoadPending("custom-farever");

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(store.InvalidPath, "malformed.json")));
        var issue = Assert.IsType<InvalidReminderEventArgs>(detected);
        Assert.Equal("malformed.json", issue.FileName);
    }

    [Fact]
    public void InvalidArchiveCollisionPreservesBothFilesAndIsReported()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var source = Path.Combine(store.InboxPath, "malformed.json");
        var destination = Path.Combine(store.InvalidPath, "malformed.json");
        File.WriteAllText(source, "{ not-json }");
        File.WriteAllText(destination, "existing data");
        InvalidReminderEventArgs? detected = null;
        store.InvalidReminderDetected += (_, args) => detected = args;

        store.LoadPending("custom-farever");
        store.LoadPending("custom-farever");
        store.LoadPending("custom-farever");

        Assert.True(File.Exists(source));
        Assert.Equal("existing data", File.ReadAllText(destination));
        var issue = Assert.IsType<InvalidReminderEventArgs>(detected);
        Assert.Contains("same name", issue.Reason);
    }

    [Fact]
    public void AtomicWriteCleansUpTemporaryFileWhenDestinationCannotBeReplaced()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(destination);

        var exception = Record.Exception(() => ReminderStore.AtomicWrite(destination, "contents"));

        Assert.True(exception is IOException or UnauthorizedAccessException);
        Assert.Empty(Directory.EnumerateFiles(_root, ".settings.json.*.tmp"));
    }

    [Fact]
    public void SaveCatalogAtomicallyPreservesStableGameId()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var original = new GameDefinition { Id = "steam-123", Name = "Old Name", Processes = ["Old.exe"] };
        store.SaveCatalog(store.LoadCatalog() with { Games = [original] });

        store.SaveCatalog(store.LoadCatalog() with
        {
            Games = [original with { Name = "New Name", Aliases = ["Speech Name"], Processes = ["New.exe"] }]
        });

        var saved = Assert.Single(store.LoadCatalog().Games);
        Assert.Equal("steam-123", saved.Id);
        Assert.Equal("New Name", saved.Name);
        Assert.Equal("Speech Name", Assert.Single(saved.Aliases));
    }

    [Fact]
    public void StaleCatalogSaveFailsWithoutOverwritingNewerRevision()
    {
        var store = new ReminderStore(_root);
        store.EnsureInitialized();
        var stale = store.LoadCatalog();
        store.SaveCatalog(stale with
        {
            Games = [new GameDefinition { Id = "newer", Name = "Newer" }]
        });

        var exception = Assert.Throws<InvalidDataException>(() => store.SaveCatalog(stale with
        {
            Games = [new GameDefinition { Id = "stale", Name = "Stale" }]
        }));

        Assert.Contains("changed after it was loaded", exception.Message);
        Assert.Equal("newer", Assert.Single(store.LoadCatalog().Games).Id);
    }

    [Fact]
    public void SyncProviderOperationRetriesTemporaryAccessFailures()
    {
        var attempts = 0;
        var waits = new List<int>();

        var result = ReminderStore.RetrySyncProviderOperation(
            () => ++attempts < 3
                ? throw new UnauthorizedAccessException("Temporarily locked.")
                : "available",
            waits.Add);

        Assert.Equal("available", result);
        Assert.Equal(3, attempts);
        Assert.Equal([1, 2], waits);
    }

    [Fact]
    public void SyncProviderOperationStopsAfterRetryLimit()
    {
        var attempts = 0;

        Assert.Throws<IOException>(() => ReminderStore.RetrySyncProviderOperation<string>(
            () =>
            {
                attempts++;
                throw new IOException("Still locked.");
            },
            _ => { },
            retryLimit: 3));

        Assert.Equal(3, attempts);
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
