using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class ScanProbeTests
{
    [Fact]
    public void SettledGamesAreSkippedSoASteadyStateSweepWalksNothing()
    {
        var catalog = Catalog(SteamGame("123", InstallState.Installed));

        var resolve = App.SelectAppIdsToResolve(
            catalog,
            Installed("123"),
            librariesFullyEnumerated: true,
            []);

        Assert.Empty(resolve);
    }

    [Fact]
    public void AnAppIdTheCatalogDoesNotKnowIsResolved()
    {
        var resolve = App.SelectAppIdsToResolve(
            new GameCatalog(),
            Installed("123"),
            librariesFullyEnumerated: true,
            []);

        Assert.Equal("123", Assert.Single(resolve));
    }

    [Theory]
    [InlineData(InstallState.Installing)]
    [InlineData(InstallState.NotInstalled)]
    public void AnAppIdTheCatalogHasNotSettledIsResolved(InstallState state)
    {
        var resolve = App.SelectAppIdsToResolve(
            Catalog(SteamGame("123", state)),
            Installed("123"),
            librariesFullyEnumerated: true,
            []);

        Assert.Equal("123", Assert.Single(resolve));
    }

    [Fact]
    public void AnAppIdThatDisappearedIsResolvedSoTheImportCanFlagIt()
    {
        var resolve = App.SelectAppIdsToResolve(
            Catalog(SteamGame("123", InstallState.Installed)),
            Installed(),
            librariesFullyEnumerated: true,
            []);

        Assert.Equal("123", Assert.Single(resolve));
    }

    [Fact]
    public void AnIncompleteProbeNeverConcludesAGameDisappeared()
    {
        var resolve = App.SelectAppIdsToResolve(
            Catalog(SteamGame("123", InstallState.Installed)),
            Installed(),
            librariesFullyEnumerated: false,
            []);

        Assert.Empty(resolve);
    }

    [Fact]
    public void AGameAlreadyFlaggedAndStillAbsentIsNotResolvedAgain()
    {
        var resolve = App.SelectAppIdsToResolve(
            Catalog(SteamGame("123", InstallState.NotInstalled)),
            Installed(),
            librariesFullyEnumerated: true,
            []);

        Assert.Empty(resolve);
    }

    [Fact]
    public void RemovedSteamGamesStayRemoved()
    {
        var resolve = App.SelectAppIdsToResolve(
            Catalog(SteamGame("123", InstallState.Installed)),
            Installed("123", "456"),
            librariesFullyEnumerated: true,
            ["123", "456"]);

        Assert.Empty(resolve);
    }

    [Fact]
    public void NonSteamEntriesNeverEnterTheSteamResolveSet()
    {
        var catalog = Catalog(new GameDefinition
        {
            Id = "custom-1",
            Name = "Everwind",
            Source = new GameSource { Type = "manual", InstallState = InstallState.NotInstalled }
        });

        var resolve = App.SelectAppIdsToResolve(catalog, Installed(), librariesFullyEnumerated: true, []);

        Assert.Empty(resolve);
    }

    private static IReadOnlySet<string> Installed(params string[] appIds) =>
        appIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static GameCatalog Catalog(params GameDefinition[] games) => new() { Games = games };

    private static GameDefinition SteamGame(string appId, InstallState state) => new()
    {
        Id = $"steam-{appId}",
        Name = $"Game {appId}",
        Processes = ["Game.exe"],
        Source = new GameSource { Type = "steam", AppId = appId, InstallState = state }
    };
}
