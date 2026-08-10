namespace GameReminders.Core;

public sealed class ReminderStore
{
    private readonly string _root;

    public ReminderStore(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string CatalogPath => Path.Combine(_root, "games.json");
    public string InboxPath => Path.Combine(_root, "inbox");
    public string CompletedPath => Path.Combine(_root, "completed");
    public string InvalidPath => Path.Combine(_root, "invalid");

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(InboxPath);
        Directory.CreateDirectory(CompletedPath);
        Directory.CreateDirectory(InvalidPath);

        if (!File.Exists(CatalogPath))
        {
            AtomicWrite(CatalogPath, JsonProtocol.WriteCatalog(new GameCatalog()));
        }
    }

    public GameCatalog LoadCatalog() => JsonProtocol.ReadCatalog(File.ReadAllText(CatalogPath));

    public IReadOnlyList<Reminder> LoadPending(string gameId)
    {
        if (!Directory.Exists(InboxPath))
        {
            return [];
        }

        var reminders = new List<Reminder>();
        foreach (var path in Directory.EnumerateFiles(InboxPath, "*.json").OrderBy(path => path))
        {
            try
            {
                var reminder = JsonProtocol.ReadReminder(File.ReadAllText(path), path);
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
                // Milestone 4 will track retries and surface invalid files in the UI.
            }
            catch (System.Text.Json.JsonException)
            {
                // A partially synchronized file is retried on the next scan.
            }
        }

        return reminders.OrderBy(reminder => reminder.CreatedAt).ToArray();
    }

    public void Complete(Reminder reminder)
    {
        if (string.IsNullOrWhiteSpace(reminder.SourcePath))
        {
            throw new InvalidOperationException("The reminder has no source path.");
        }

        Directory.CreateDirectory(CompletedPath);
        var destination = Path.Combine(CompletedPath, Path.GetFileName(reminder.SourcePath));
        File.Move(reminder.SourcePath, destination, overwrite: false);
    }

    public static void AtomicWrite(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
