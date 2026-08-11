namespace GameReminders.Core;

public sealed class ReminderStore
{
    private const int InvalidFileRetryLimit = 3;
    private const int SyncProviderRetryLimit = 4;
    private readonly string _root;
    private readonly Dictionary<string, InvalidFileAttempts> _invalidFileAttempts =
        new(StringComparer.OrdinalIgnoreCase);

    public ReminderStore(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string CatalogPath => Path.Combine(_root, "games.json");
    public string InboxPath => Path.Combine(_root, "inbox");
    public string CompletedPath => Path.Combine(_root, "completed");
    public string InvalidPath => Path.Combine(_root, "invalid");

    public event EventHandler<InvalidReminderEventArgs>? InvalidReminderDetected;

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(InboxPath);
        Directory.CreateDirectory(CompletedPath);
        Directory.CreateDirectory(InvalidPath);

        if (!File.Exists(CatalogPath))
        {
            RetrySyncProviderOperation(() =>
            {
                AtomicWrite(CatalogPath, JsonProtocol.WriteCatalog(new GameCatalog()));
                return true;
            });
        }
    }

    public GameCatalog LoadCatalog()
    {
        var json = RetrySyncProviderOperation(() => File.ReadAllText(CatalogPath));
        var catalog = JsonProtocol.ReadCatalog(json);
        if (!JsonProtocol.IsEmptyCatalogPlaceholder(json))
        {
            return catalog;
        }

        SaveCatalog(catalog);
        return JsonProtocol.ReadCatalog(
            RetrySyncProviderOperation(() => File.ReadAllText(CatalogPath)));
    }

    public void SaveCatalog(GameCatalog catalog)
    {
        RetrySyncProviderOperation(() =>
        {
            var current = JsonProtocol.ReadCatalog(File.ReadAllText(CatalogPath));
            if (current.UpdatedAt != catalog.UpdatedAt)
            {
                throw new InvalidDataException(
                    "games.json changed after it was loaded. Reload the catalog and try again; no data was overwritten.");
            }

            var updatedAt = DateTimeOffset.UtcNow;
            if (updatedAt <= current.UpdatedAt)
            {
                updatedAt = current.UpdatedAt.AddTicks(1);
            }

            AtomicWrite(CatalogPath, JsonProtocol.WriteCatalog(catalog with { UpdatedAt = updatedAt }));
            return true;
        });
    }

    public IReadOnlyList<Reminder> LoadPending(string gameId)
    {
        if (!Directory.Exists(InboxPath))
        {
            return [];
        }

        string[] paths;
        try
        {
            paths = Directory.EnumerateFiles(InboxPath, "*.json").OrderBy(path => path).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var reminders = new List<Reminder>();
        foreach (var path in paths)
        {
            try
            {
                var reminder = JsonProtocol.ReadReminder(File.ReadAllText(path), path);
                _invalidFileAttempts.Remove(path);
                if (string.Equals(reminder.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                {
                    reminders.Add(reminder);
                }
            }
            catch (IOException)
            {
                // A sync provider may hold a file briefly. The next scan retries it.
            }
            catch (UnauthorizedAccessException)
            {
                // Treat a temporary cloud-provider lock as retryable.
            }
            catch (InvalidDataException)
            {
                TrackInvalidReminder(path);
            }
            catch (System.Text.Json.JsonException)
            {
                TrackInvalidReminder(path);
            }
        }

        return reminders.OrderBy(reminder => reminder.CreatedAt).ToArray();
    }

    private void TrackInvalidReminder(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                _invalidFileAttempts.Remove(path);
                return;
            }

            var signature = new InvalidFileSignature(info.Length, info.LastWriteTimeUtc);
            if (!_invalidFileAttempts.TryGetValue(path, out var attempts) || attempts.Signature != signature)
            {
                attempts = new InvalidFileAttempts(signature, 0, false);
            }

            attempts = attempts with { Count = attempts.Count + 1 };
            _invalidFileAttempts[path] = attempts;
            if (attempts.Count < InvalidFileRetryLimit)
            {
                return;
            }

            Directory.CreateDirectory(InvalidPath);
            var destination = Path.Combine(InvalidPath, Path.GetFileName(path));
            if (File.Exists(destination))
            {
                if (!attempts.Reported)
                {
                    InvalidReminderDetected?.Invoke(
                        this,
                        new InvalidReminderEventArgs(
                            Path.GetFileName(path),
                            "could not be quarantined because a file with the same name already exists in invalid"));
                    _invalidFileAttempts[path] = attempts with { Reported = true };
                }

                return;
            }

            File.Move(path, destination, overwrite: false);
            _invalidFileAttempts.Remove(path);
            InvalidReminderDetected?.Invoke(
                this,
                new InvalidReminderEventArgs(
                    Path.GetFileName(path),
                    "was moved to invalid after repeated parse failures"));
        }
        catch (IOException)
        {
            // A sync-provider operation interrupted quarantine. Retry on the next scan.
        }
        catch (UnauthorizedAccessException)
        {
            // A temporary provider lock can also affect file metadata or the move.
        }
    }

    public void Complete(Reminder reminder)
    {
        if (string.IsNullOrWhiteSpace(reminder.SourcePath))
        {
            throw new InvalidOperationException("The reminder has no source path.");
        }

        Directory.CreateDirectory(CompletedPath);
        var fileName = Path.GetFileName(reminder.SourcePath);
        var destination = Path.Combine(CompletedPath, fileName);
        RemoveMatchingRootDuplicate(reminder, fileName);

        if (File.Exists(destination))
        {
            CompleteFromExistingArchive(reminder, destination, reminder.SourcePath);
            return;
        }

        var temporaryPath = Path.Combine(CompletedPath, $".{fileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(reminder.SourcePath, temporaryPath, overwrite: false);
            var staged = JsonProtocol.ReadReminder(File.ReadAllText(temporaryPath), temporaryPath);
            if (!HasSamePayload(staged, reminder))
            {
                throw new InvalidDataException(
                    $"Completed reminder '{fileName}' did not match the pending reminder after it was copied.");
            }

            try
            {
                File.Move(temporaryPath, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                EnsureSameArchive(destination, reminder);
            }

            RetrySyncProviderOperation(() =>
            {
                File.Delete(reminder.SourcePath);
                return true;
            });
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the archive failure; cleanup is best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // A provider lock may briefly prevent cleanup.
            }
        }
    }

    private void RemoveMatchingRootDuplicate(Reminder reminder, string fileName)
    {
        var rootDuplicate = Path.Combine(_root, fileName);
        if (!File.Exists(rootDuplicate))
        {
            return;
        }

        var duplicate = JsonProtocol.ReadReminder(
            RetrySyncProviderOperation(() => File.ReadAllText(rootDuplicate)),
            rootDuplicate);
        if (!HasSamePayload(duplicate, reminder))
        {
            throw new InvalidDataException(
                $"Root reminder '{fileName}' conflicts with the pending reminder and was preserved.");
        }

        RetrySyncProviderOperation(() =>
        {
            File.Delete(rootDuplicate);
            return true;
        });
    }

    private void CompleteFromExistingArchive(Reminder reminder, string destination, string sourcePath)
    {
        EnsureSameArchive(destination, reminder);
        RetrySyncProviderOperation(() =>
        {
            File.Delete(sourcePath);
            return true;
        });
    }

    private static void EnsureSameArchive(string destination, Reminder reminder)
    {
        var archived = JsonProtocol.ReadReminder(
            RetrySyncProviderOperation(() => File.ReadAllText(destination)),
            destination);
        if (!HasSamePayload(archived, reminder))
        {
            throw new InvalidDataException(
                $"Completed reminder '{Path.GetFileName(destination)}' conflicts with the pending reminder.");
        }
    }

    private static bool HasSamePayload(Reminder left, Reminder right) =>
        left.SchemaVersion == right.SchemaVersion &&
        left.Id == right.Id &&
        string.Equals(left.GameId, right.GameId, StringComparison.Ordinal) &&
        string.Equals(left.GameNameAtCreation, right.GameNameAtCreation, StringComparison.Ordinal) &&
        string.Equals(left.Message, right.Message, StringComparison.Ordinal) &&
        left.CreatedAt == right.CreatedAt;

    public static void AtomicWrite(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the original write/move failure; cleanup is best-effort.
            }
            catch (UnauthorizedAccessException)
            {
                // A provider lock may briefly prevent cleanup.
            }
        }
    }

    internal static T RetrySyncProviderOperation<T>(
        Func<T> operation,
        Action<int>? wait = null,
        int retryLimit = SyncProviderRetryLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryLimit, 1);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (
                attempt < retryLimit &&
                exception is IOException or UnauthorizedAccessException)
            {
                (wait ?? WaitBeforeRetry)(attempt);
            }
        }
    }

    private static void WaitBeforeRetry(int attempt) =>
        Thread.Sleep(TimeSpan.FromMilliseconds(100 * (1 << (attempt - 1))));

    private sealed record InvalidFileAttempts(InvalidFileSignature Signature, int Count, bool Reported);

    private readonly record struct InvalidFileSignature(long Length, DateTime LastWriteTimeUtc);
}

public sealed class InvalidReminderEventArgs(string fileName, string reason) : EventArgs
{
    public string FileName { get; } = fileName;
    public string Reason { get; } = reason;
}
