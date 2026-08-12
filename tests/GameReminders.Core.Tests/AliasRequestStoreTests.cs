using GameReminders.Core;

namespace GameReminders.Core.Tests;

public sealed class AliasRequestStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"game-reminders-alias-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LoadPendingDeduplicatesExactRequestIdsWithoutDeletingCopies()
    {
        var (requests, _, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "first.json", request);
        Write(requests.InboxPath, "second.json", request);

        var loaded = Assert.Single(requests.LoadPending());

        Assert.Equal(request.Id, loaded.Id);
        Assert.Equal(2, Directory.EnumerateFiles(requests.InboxPath, "*.json").Count());
    }

    [Fact]
    public void ConflictingDuplicateIdsArePreservedAndReported()
    {
        var (requests, _, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "first.json", request);
        Write(requests.InboxPath, "second.json", request with { Alias = "Different" });
        var issues = new List<AliasRequestIssueEventArgs>();
        requests.IssueDetected += (_, args) => issues.Add(args);

        Assert.Empty(requests.LoadPending());
        Assert.Equal(2, issues.Count);
        Assert.Equal(2, Directory.EnumerateFiles(requests.InboxPath, "*.json").Count());
    }

    [Fact]
    public void AutoAcceptPreservesAndReturnsUnknownGameRequestAsFailure()
    {
        var (requests, reminders, _) = CreateStores();
        var request = CreateRequest() with { GameId = "missing" };
        Write(requests.InboxPath, "unknown.json", request);
        var result = requests.AutoAcceptPending(reminders);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(request.Id, failure.Request.Id);
        Assert.Contains("no longer exists", failure.Reason);
        Assert.Single(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
    }

    [Fact]
    public void AutoAcceptAddsEveryValidAliasWithoutUserAction()
    {
        var (requests, reminders, _) = CreateStores();
        var first = CreateRequest();
        var second = CreateRequest() with
        {
            Id = Guid.Parse("acde4a04-4442-4f07-823d-50ecf006c097"),
            Alias = "Fare-ever"
        };
        Write(requests.InboxPath, "first.json", first);
        Write(requests.InboxPath, "second.json", second);

        var result = requests.AutoAcceptPending(reminders);

        Assert.Equal(2, result.AcceptedCount);
        Assert.Empty(result.Failures);
        Assert.Equal(["Fare ever"], Assert.Single(reminders.LoadCatalog().Games).Aliases);
        Assert.Empty(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.Equal(2, Directory.EnumerateFiles(requests.AcceptedPath, "*.json").Count());
    }

    [Fact]
    public void MalformedRequestIsReportedWithoutHidingValidRequests()
    {
        var (requests, _, _) = CreateStores();
        Write(requests.InboxPath, "valid.json", CreateRequest());
        File.WriteAllText(Path.Combine(requests.InboxPath, "malformed.json"), "{ nope }");
        AliasRequestIssueEventArgs? issue = null;
        requests.IssueDetected += (_, args) => issue = args;

        Assert.Single(requests.LoadPending());
        Assert.Equal("malformed.json", Assert.IsType<AliasRequestIssueEventArgs>(issue).FileName);
        Assert.True(File.Exists(Path.Combine(requests.InboxPath, "malformed.json")));
    }

    [Fact]
    public void AcceptAddsAliasAndArchivesEveryExactDuplicate()
    {
        var (requests, reminders, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "first.json", request);
        Write(requests.InboxPath, "second.json", request);
        var selected = Assert.Single(requests.LoadPending());

        requests.Accept(selected, reminders);

        Assert.Contains("Fare ever", Assert.Single(reminders.LoadCatalog().Games).Aliases);
        Assert.Empty(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.Equal(2, Directory.EnumerateFiles(requests.AcceptedPath, "*.json").Count());
    }

    [Fact]
    public void AcceptIsIdempotentWhenAliasAlreadyBelongsToSelectedGame()
    {
        var (requests, reminders, _) = CreateStores(["Fare ever"]);
        var request = CreateRequest();
        Write(requests.InboxPath, "request.json", request);

        requests.Accept(Assert.Single(requests.LoadPending()), reminders);

        Assert.Equal(["Fare ever"], Assert.Single(reminders.LoadCatalog().Games).Aliases);
        Assert.Single(Directory.EnumerateFiles(requests.AcceptedPath, "*.json"));
    }

    [Fact]
    public void AliasCollisionPreservesRequestAndCatalog()
    {
        var (requests, reminders, _) = CreateStores();
        var other = new GameDefinition
        {
            Id = "other",
            Name = "Other",
            Aliases = ["Fare-ever"]
        };
        reminders.SaveCatalog(reminders.LoadCatalog() with
        {
            Games = reminders.LoadCatalog().Games.Concat([other]).ToArray()
        });
        var request = CreateRequest();
        Write(requests.InboxPath, "request.json", request);

        var exception = Assert.Throws<InvalidDataException>(() =>
            requests.Accept(Assert.Single(requests.LoadPending()), reminders));

        Assert.Contains("another game", exception.Message);
        Assert.Single(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.DoesNotContain("Fare ever", reminders.LoadCatalog().Games[0].Aliases);
    }

    [Fact]
    public void ConcurrentCatalogChangePreservesRequest()
    {
        var (requests, reminders, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "request.json", request);
        var selected = Assert.Single(requests.LoadPending());

        var exception = Assert.Throws<InvalidDataException>(() =>
            requests.Accept(selected, reminders, stale =>
                reminders.SaveCatalog(stale with
                {
                    Games = stale.Games.Select(game => game with { Name = "Renamed" }).ToArray()
                })));

        Assert.Contains("changed after it was loaded", exception.Message);
        Assert.Single(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.Equal("Renamed", Assert.Single(reminders.LoadCatalog().Games).Name);
    }

    [Fact]
    public void RejectPreservesPendingRequestWhenArchiveConflicts()
    {
        var (requests, _, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "request.json", request);
        Write(requests.RejectedPath, "request.json", request with { Alias = "Conflicting" });
        var selected = Assert.Single(requests.LoadPending());

        Assert.Throws<InvalidDataException>(() => requests.Reject(selected));

        Assert.Single(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.Single(Directory.EnumerateFiles(requests.RejectedPath, "*.json"));
    }

    [Fact]
    public void RejectCompletesAfterAnIdenticalArchiveAlreadyExists()
    {
        var (requests, _, _) = CreateStores();
        var request = CreateRequest();
        Write(requests.InboxPath, "request.json", request);
        Write(requests.RejectedPath, "request.json", request);

        requests.Reject(Assert.Single(requests.LoadPending()));

        Assert.Empty(Directory.EnumerateFiles(requests.InboxPath, "*.json"));
        Assert.Single(Directory.EnumerateFiles(requests.RejectedPath, "*.json"));
    }

    private (AliasRequestStore Requests, ReminderStore Reminders, GameCatalog Catalog) CreateStores(
        IReadOnlyList<string>? aliases = null)
    {
        var reminders = new ReminderStore(_root);
        reminders.EnsureInitialized();
        var game = new GameDefinition
        {
            Id = "custom-farever",
            Name = "Farever",
            Aliases = aliases ?? []
        };
        reminders.SaveCatalog(reminders.LoadCatalog() with { Games = [game] });
        var requests = new AliasRequestStore(_root);
        requests.EnsureInitialized();
        return (requests, reminders, reminders.LoadCatalog());
    }

    private static AliasRequest CreateRequest() => new()
    {
        Id = Guid.Parse("9f6db96e-1c50-4785-91d6-94580d2ab833"),
        GameId = "custom-farever",
        Alias = "Fare ever",
        CreatedAt = DateTimeOffset.Parse("2026-08-12T08:00:00Z")
    };

    private static void Write(string directory, string fileName, AliasRequest request)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), JsonProtocol.WriteAliasRequest(request));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
