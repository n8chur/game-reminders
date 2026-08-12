namespace GameReminders.Core;

public sealed class AliasRequestStore
{
    private readonly string _root;
    private readonly HashSet<string> _reportedIssues = new(StringComparer.OrdinalIgnoreCase);

    public AliasRequestStore(string root)
    {
        _root = Path.GetFullPath(root);
    }

    public string InboxPath => Path.Combine(_root, "alias-requests", "inbox");
    public string AcceptedPath => Path.Combine(_root, "alias-requests", "accepted");
    public string RejectedPath => Path.Combine(_root, "alias-requests", "rejected");

    public event EventHandler<AliasRequestIssueEventArgs>? IssueDetected;

    public void EnsureInitialized()
    {
        Directory.CreateDirectory(InboxPath);
        Directory.CreateDirectory(AcceptedPath);
        Directory.CreateDirectory(RejectedPath);
    }

    public IReadOnlyList<AliasRequest> LoadPending()
    {
        EnsureInitialized();

        var parsed = new List<AliasRequest>();
        foreach (var path in ReminderStore.RetrySyncProviderOperation(() =>
                     Directory.EnumerateFiles(InboxPath, "*.json").OrderBy(path => path).ToArray()))
        {
            try
            {
                parsed.Add(Read(path));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                Report(path, exception.Message);
            }
        }

        var result = new List<AliasRequest>();
        foreach (var group in parsed.GroupBy(request => request.Id))
        {
            var first = group.First();
            if (group.Any(request => !HasSamePayload(first, request)))
            {
                foreach (var request in group)
                {
                    Report(request.SourcePath!, $"conflicts with another alias request using id '{request.Id:D}'");
                }

                continue;
            }

            result.Add(first);
        }

        return result.OrderBy(request => request.CreatedAt).ToArray();
    }

    public AliasRequestProcessingResult AutoAcceptPending(ReminderStore reminderStore)
    {
        ArgumentNullException.ThrowIfNull(reminderStore);
        var acceptedCount = 0;
        var failures = new List<AliasRequestFailure>();
        foreach (var request in LoadPending())
        {
            try
            {
                Accept(request, reminderStore);
                acceptedCount++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
            {
                failures.Add(new AliasRequestFailure(request, exception.Message));
            }
        }

        return new AliasRequestProcessingResult(acceptedCount, failures);
    }

    public void Accept(AliasRequest request, ReminderStore reminderStore) =>
        Accept(request, reminderStore, beforeCatalogSave: null);

    internal void Accept(
        AliasRequest request,
        ReminderStore reminderStore,
        Action<GameCatalog>? beforeCatalogSave)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reminderStore);
        var copies = ResolveCurrentCopies(request);
        var catalog = reminderStore.LoadCatalog();
        var game = catalog.Games.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, request.GameId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                $"Alias request '{request.Id:D}' references a game that no longer exists.");

        var alias = request.Alias.Trim();
        var normalized = NameNormalizer.Normalize(alias);
        if (normalized.Length == 0)
        {
            throw new InvalidDataException($"Alias request '{request.Id:D}' contains an empty alias.");
        }

        var owners = catalog.Games.Where(candidate =>
                new[] { candidate.Name }.Concat(candidate.Aliases)
                    .Any(value => NameNormalizer.Normalize(value) == normalized))
            .ToArray();
        if (owners.Any(owner => !string.Equals(owner.Id, game.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Alias '{alias}' already resolves to another game. The request was preserved.");
        }

        if (owners.Length == 0)
        {
            var updatedGame = game with { Aliases = game.Aliases.Concat([alias]).ToArray() };
            beforeCatalogSave?.Invoke(catalog);
            reminderStore.SaveCatalog(catalog with
            {
                Games = catalog.Games.Select(candidate =>
                    string.Equals(candidate.Id, game.Id, StringComparison.OrdinalIgnoreCase)
                        ? updatedGame
                        : candidate).ToArray()
            });
        }

        ArchiveCopies(copies, AcceptedPath);
    }

    public void Reject(AliasRequest request) =>
        ArchiveCopies(ResolveCurrentCopies(request), RejectedPath);

    private AliasRequest Read(string path) =>
        JsonProtocol.ReadAliasRequest(
            ReminderStore.RetrySyncProviderOperation(() => File.ReadAllText(path)),
            path);

    private IReadOnlyList<AliasRequest> ResolveCurrentCopies(AliasRequest selected)
    {
        if (string.IsNullOrWhiteSpace(selected.SourcePath))
        {
            throw new InvalidOperationException("The alias request has no source path.");
        }

        var copies = new List<AliasRequest>();
        foreach (var path in ReminderStore.RetrySyncProviderOperation(() =>
                     Directory.EnumerateFiles(InboxPath, "*.json").OrderBy(path => path).ToArray()))
        {
            try
            {
                var candidate = Read(path);
                if (candidate.Id == selected.Id)
                {
                    copies.Add(candidate);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                Report(path, exception.Message);
            }
        }
        if (copies.Count == 0)
        {
            throw new FileNotFoundException(
                $"Alias request '{selected.Id:D}' is no longer pending.",
                selected.SourcePath);
        }

        if (copies.Any(candidate => !HasSamePayload(candidate, selected)))
        {
            throw new InvalidDataException(
                $"Alias request id '{selected.Id:D}' has conflicting files. All copies were preserved.");
        }

        return copies;
    }

    private static void ArchiveCopies(IReadOnlyList<AliasRequest> copies, string archiveDirectory)
    {
        Directory.CreateDirectory(archiveDirectory);
        foreach (var request in copies)
        {
            var source = request.SourcePath
                ?? throw new InvalidOperationException("The alias request has no source path.");
            var destination = Path.Combine(archiveDirectory, Path.GetFileName(source));
            if (File.Exists(destination))
            {
                var archived = JsonProtocol.ReadAliasRequest(
                    ReminderStore.RetrySyncProviderOperation(() => File.ReadAllText(destination)),
                    destination);
                if (!HasSamePayload(archived, request))
                {
                    throw new InvalidDataException(
                        $"Archived alias request '{Path.GetFileName(source)}' conflicts with the pending request. Both files were preserved.");
                }

                ReminderStore.RetrySyncProviderOperation(() =>
                {
                    File.Delete(source);
                    return true;
                });
                continue;
            }

            var temporaryPath = Path.Combine(
                archiveDirectory,
                $".{Path.GetFileName(source)}.{Guid.NewGuid():N}.tmp");
            try
            {
                ReminderStore.RetrySyncProviderOperation(() =>
                {
                    File.Copy(source, temporaryPath, overwrite: true);
                    return true;
                });
                var staged = JsonProtocol.ReadAliasRequest(
                    ReminderStore.RetrySyncProviderOperation(() => File.ReadAllText(temporaryPath)),
                    temporaryPath);
                if (!HasSamePayload(staged, request))
                {
                    throw new InvalidDataException(
                        $"Archived alias request '{Path.GetFileName(source)}' changed while it was copied.");
                }

                ReminderStore.RetrySyncProviderOperation(() =>
                {
                    File.Move(temporaryPath, destination, overwrite: false);
                    return true;
                });
                ReminderStore.RetrySyncProviderOperation(() =>
                {
                    File.Delete(source);
                    return true;
                });
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void Report(string path, string reason)
    {
        var key = $"{path}\n{reason}";
        if (_reportedIssues.Add(key))
        {
            IssueDetected?.Invoke(
                this,
                new AliasRequestIssueEventArgs(Path.GetFileName(path), reason));
        }
    }

    private static bool HasSamePayload(AliasRequest left, AliasRequest right) =>
        left.SchemaVersion == right.SchemaVersion &&
        left.Id == right.Id &&
        string.Equals(left.GameId, right.GameId, StringComparison.Ordinal) &&
        string.Equals(left.Alias, right.Alias, StringComparison.Ordinal) &&
        left.CreatedAt == right.CreatedAt;
}

public sealed class AliasRequestIssueEventArgs(string fileName, string reason) : EventArgs
{
    public string FileName { get; } = fileName;
    public string Reason { get; } = reason;
}

public sealed record AliasRequestFailure(AliasRequest Request, string Reason);

public sealed record AliasRequestProcessingResult(
    int AcceptedCount,
    IReadOnlyList<AliasRequestFailure> Failures);
