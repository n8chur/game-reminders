using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class SteamInstallWatcherTests
{
    [Fact]
    public void PendingAppIdsSelectsOnlyInstallingSteamGames()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                InstallingGame("steam-123", "123"),
                InstallingGame("steam-456", "456") with
                {
                    Source = new GameSource { Type = "steam", AppId = "456", InstallState = InstallState.Installed }
                },
                new GameDefinition
                {
                    Id = "custom-manual",
                    Name = "Manual",
                    Source = new GameSource { Type = "manual", InstallState = InstallState.Installing }
                },
                new GameDefinition
                {
                    Id = "steam-blank",
                    Name = "Blank",
                    Source = new GameSource { Type = "steam", AppId = " ", InstallState = InstallState.Installing }
                },
                new GameDefinition { Id = "custom-sourceless", Name = "Sourceless" }
            ]
        };

        var pending = SteamInstallWatcher.PendingAppIds(catalog);

        Assert.Equal("123", Assert.Single(pending));
    }

    [Fact]
    public void WatcherPollsOnlyWhileAGameIsInstalling()
    {
        var requests = new List<IReadOnlySet<string>>();
        using var watcher = new SteamInstallWatcher(requests.Add, TimeSpan.FromMinutes(5));

        watcher.PollOnce();
        Assert.Empty(requests);
        Assert.False(watcher.IsPolling);

        watcher.Update(new GameCatalog { Games = [InstallingGame("steam-123", "123")] });
        Assert.True(watcher.IsPolling);
        watcher.PollOnce();
        Assert.Equal("123", Assert.Single(Assert.Single(requests)));

        watcher.Update(new GameCatalog { Games = [] });
        Assert.False(watcher.IsPolling);
        watcher.PollOnce();
        Assert.Single(requests);
    }

    [Fact]
    public void DisposedWatcherStopsPolling()
    {
        var requests = new List<IReadOnlySet<string>>();
        var watcher = new SteamInstallWatcher(requests.Add, TimeSpan.FromMinutes(5));
        watcher.Update(new GameCatalog { Games = [InstallingGame("steam-123", "123")] });

        watcher.Dispose();
        watcher.PollOnce();

        Assert.Empty(requests);
        Assert.False(watcher.IsPolling);
    }

    private static GameDefinition InstallingGame(string id, string appId) => new()
    {
        Id = id,
        Name = appId,
        Source = new GameSource { Type = "steam", AppId = appId, InstallState = InstallState.Installing }
    };
}
