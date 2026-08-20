using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class SteamCatalogImporterTests
{
    [Fact]
    public void TrustedSteamGamesAreAddedWithStableIds()
    {
        var detection = SteamDetection("123", "Test Game", "TestGame.exe");

        var result = SteamCatalogImporter.Import(new GameCatalog(), [detection]);

        var game = Assert.Single(result.AddedGames);
        Assert.Equal("steam-123", game.Id);
        Assert.Equal("123", game.Source?.AppId);
        Assert.Equal("TestGame.exe", Assert.Single(game.Processes));
        Assert.Single(result.Catalog.Games);
    }

    [Fact]
    public void ImportSkipsExistingAppIdsAndRemovesConflictingProcesses()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition
                {
                    Id = "custom-existing",
                    Name = "Existing",
                    Processes = ["Existing.exe"],
                    Source = new GameSource { Type = "steam", AppId = "123" }
                }
            ]
        };

        var result = SteamCatalogImporter.Import(catalog,
        [
            SteamDetection("123", "Same app", "Other.exe"),
            SteamDetection("456", "Same process", "existing.EXE")
        ]);

        var added = Assert.Single(result.AddedGames);
        Assert.Equal("Same process", added.Name);
        Assert.Empty(added.Processes);
        Assert.Equal(2, result.Catalog.Games.Count);
    }

    [Fact]
    public void BatchImportKeepsBothGamesWithoutDuplicateProcessMappings()
    {
        var result = SteamCatalogImporter.Import(new GameCatalog(),
        [
            SteamDetection("123", "First", "Shared.exe"),
            SteamDetection("456", "Second", "shared.EXE")
        ]);

        Assert.Equal(2, result.AddedGames.Count);
        Assert.Equal("Shared.exe", Assert.Single(result.AddedGames[0].Processes));
        Assert.Empty(result.AddedGames[1].Processes);
    }

    [Fact]
    public void SuppressedSteamGameIsNotReadded()
    {
        var result = SteamCatalogImporter.Import(
            new GameCatalog(),
            [SteamDetection("123", "Removed", "Removed.exe")],
            ["123"]);

        Assert.Empty(result.AddedGames);
        Assert.Empty(result.Catalog.Games);
    }

    [Fact]
    public void AmbiguousExecutableIsAddedButMarkedForReview()
    {
        var detection = SteamDetection("123", "Ambiguous", string.Empty) with
        {
            Processes = [],
            CandidateProcesses = [@"Ambiguous\Binaries\Game.exe", @"Ambiguous\Launcher.exe"],
            RequiresExecutableReview = true
        };

        var result = SteamCatalogImporter.Import(new GameCatalog(), [detection]);

        var game = Assert.Single(result.GamesNeedingExecutableReview);
        Assert.True(game.Source?.RequiresExecutableReview);
        Assert.Empty(game.Processes);
        Assert.Equal(2, game.Source?.ExecutableCandidates.Count);
    }

    [Fact]
    public void RescanRepairsExistingSteamGameThatStillNeedsExecutableReview()
    {
        var existing = new GameDefinition
        {
            Id = "steam-123",
            Name = "Everwind",
            Source = new GameSource
            {
                Type = "steam",
                AppId = "123",
                RequiresExecutableReview = true,
                ExecutableCandidates = [@"Everwind\Launcher.exe"]
            }
        };
        var detection = SteamDetection("123", "Everwind", @"Everwind\Everwind.exe") with
        {
            CandidateProcesses = [@"Everwind\Everwind.exe", @"Everwind\Launcher.exe"]
        };

        var result = SteamCatalogImporter.Import(new GameCatalog { Games = [existing] }, [detection]);

        var updated = Assert.Single(result.UpdatedGames);
        Assert.Equal(@"Everwind\Everwind.exe", Assert.Single(updated.Processes));
        Assert.False(updated.Source?.RequiresExecutableReview);
        Assert.Equal(2, updated.Source?.ExecutableCandidates.Count);
        Assert.Empty(result.AddedGames);
    }

    [Fact]
    public void InstallingGameIsAddedWithoutRequiringExecutableReview()
    {
        var detection = InstallingSteamDetection("123", "Everwind");

        var result = SteamCatalogImporter.Import(new GameCatalog(), [detection]);

        var game = Assert.Single(result.AddedGames);
        Assert.Empty(game.Processes);
        Assert.Equal(InstallState.Installing, game.Source?.InstallState);
        Assert.False(game.Source?.RequiresExecutableReview);
        Assert.Empty(result.GamesNeedingExecutableReview);
        Assert.Same(game, Assert.Single(result.InstallingGames));
        Assert.Empty(result.CompletedInstalls);
    }

    [Fact]
    public void RescanOfUnchangedInstallingGameDoesNotRewriteTheCatalog()
    {
        var existing = InstallingGame("123", "Everwind");

        var result = SteamCatalogImporter.Import(
            new GameCatalog { Games = [existing] },
            [InstallingSteamDetection("123", "Everwind")]);

        Assert.Empty(result.AddedGames);
        Assert.Empty(result.UpdatedGames);
        Assert.Empty(result.CompletedInstalls);
        Assert.Same(existing, Assert.Single(result.InstallingGames));
    }

    [Fact]
    public void CompletedInstallResolvesItsExecutableAndClearsBothFlags()
    {
        var existing = InstallingGame("123", "Everwind");
        var detection = SteamDetection("123", "Everwind", @"Everwind\Everwind.exe") with
        {
            CandidateProcesses = [@"Everwind\Everwind.exe"]
        };

        var result = SteamCatalogImporter.Import(new GameCatalog { Games = [existing] }, [detection]);

        var updated = Assert.Single(result.UpdatedGames);
        Assert.Equal(@"Everwind\Everwind.exe", Assert.Single(updated.Processes));
        Assert.Equal(InstallState.Installed, updated.Source?.InstallState);
        Assert.False(updated.Source?.RequiresExecutableReview);
        Assert.Same(updated, Assert.Single(result.CompletedInstalls));
        Assert.Empty(result.InstallingGames);
    }

    [Fact]
    public void CompletedInstallWithAmbiguousExecutableFallsBackToExecutableReview()
    {
        var existing = InstallingGame("123", "Everwind");
        var detection = InstallingSteamDetection("123", "Everwind") with
        {
            InstallState = InstallState.Installed,
            RequiresExecutableReview = true,
            CandidateProcesses = [@"Everwind\Launcher.exe", @"Everwind\Editor.exe"]
        };

        var result = SteamCatalogImporter.Import(new GameCatalog { Games = [existing] }, [detection]);

        var updated = Assert.Single(result.UpdatedGames);
        Assert.Empty(updated.Processes);
        Assert.Equal(InstallState.Installed, updated.Source?.InstallState);
        Assert.True(updated.Source?.RequiresExecutableReview);
        Assert.Empty(result.InstallingGames);
    }

    [Fact]
    public void StuckExecutableReviewEntryBecomesInstallingWhenSteamIsStillDownloading()
    {
        var existing = new GameDefinition
        {
            Id = "steam-123",
            Name = "Everwind",
            Source = new GameSource { Type = "steam", AppId = "123", RequiresExecutableReview = true }
        };

        var result = SteamCatalogImporter.Import(
            new GameCatalog { Games = [existing] },
            [InstallingSteamDetection("123", "Everwind")]);

        var updated = Assert.Single(result.UpdatedGames);
        Assert.Equal(InstallState.Installing, updated.Source?.InstallState);
        Assert.False(updated.Source?.RequiresExecutableReview);
        Assert.Empty(result.CompletedInstalls);
    }

    [Fact]
    public void CancelledInstallIsRetractedRatherThanLeftStuck()
    {
        var catalog = new GameCatalog { Games = [InstallingGame("123", "Everwind"), ConfiguredGame("456", "Other")] };

        var result = SteamCatalogImporter.Import(catalog, [], null, librariesFullyEnumerated: true);

        var retracted = Assert.Single(result.RetractedGames);
        Assert.Equal("steam-123", retracted.Id);
        var remaining = Assert.Single(result.Catalog.Games);
        Assert.Equal("steam-456", remaining.Id);
    }

    [Fact]
    public void UninstalledConfiguredGameIsFlaggedButKeepsItsMapping()
    {
        var catalog = new GameCatalog { Games = [ConfiguredGame("456", "Other")] };

        var result = SteamCatalogImporter.Import(catalog, [], null, librariesFullyEnumerated: true);

        Assert.Empty(result.RetractedGames);
        var updated = Assert.Single(result.UpdatedGames);
        Assert.Equal(InstallState.NotInstalled, updated.Source?.InstallState);
        Assert.Equal(@"Other\Other.exe", Assert.Single(updated.Processes));
    }

    [Fact]
    public void ReinstalledGameReturnsToInstalledEvenWhenAlreadyConfigured()
    {
        var existing = ConfiguredGame("456", "Other") with
        {
            Source = new GameSource
            {
                Type = "steam",
                AppId = "456",
                InstallState = InstallState.NotInstalled
            }
        };
        var detection = SteamDetection("456", "Other", @"Other\Other.exe");

        var result = SteamCatalogImporter.Import(
            new GameCatalog { Games = [existing] },
            [detection],
            null,
            librariesFullyEnumerated: true);

        var updated = Assert.Single(result.UpdatedGames);
        Assert.Equal(InstallState.Installed, updated.Source?.InstallState);
        Assert.Equal(@"Other\Other.exe", Assert.Single(updated.Processes));
        Assert.Empty(result.RetractedGames);
    }

    [Fact]
    public void IncompleteScanReconcilesNothing()
    {
        var catalog = new GameCatalog { Games = [InstallingGame("123", "Everwind"), ConfiguredGame("456", "Other")] };

        var result = SteamCatalogImporter.Import(catalog, [], null, librariesFullyEnumerated: false);

        Assert.Empty(result.RetractedGames);
        Assert.Empty(result.UpdatedGames);
        Assert.Equal(2, result.Catalog.Games.Count);
    }

    [Fact]
    public void TargetedScanReconcilesOnlyTheAppIdsItLookedFor()
    {
        var catalog = new GameCatalog { Games = [InstallingGame("123", "Everwind"), ConfiguredGame("456", "Other")] };

        var result = SteamCatalogImporter.Import(
            catalog,
            [],
            null,
            librariesFullyEnumerated: true,
            scannedAppIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "123" });

        Assert.Equal("steam-123", Assert.Single(result.RetractedGames).Id);
        Assert.Empty(result.UpdatedGames);
        Assert.Equal("steam-456", Assert.Single(result.Catalog.Games).Id);
    }

    [Fact]
    public void AlreadyFlaggedGameIsNotRewrittenOnEveryScan()
    {
        var existing = ConfiguredGame("456", "Other") with
        {
            Source = new GameSource
            {
                Type = "steam",
                AppId = "456",
                InstallState = InstallState.NotInstalled
            }
        };

        var result = SteamCatalogImporter.Import(
            new GameCatalog { Games = [existing] },
            [],
            null,
            librariesFullyEnumerated: true);

        Assert.Empty(result.UpdatedGames);
        Assert.Empty(result.RetractedGames);
    }

    private static GameDefinition ConfiguredGame(string appId, string name) => new()
    {
        Id = $"steam-{appId}",
        Name = name,
        Processes = [$@"{name}\{name}.exe"],
        Source = new GameSource { Type = "steam", AppId = appId }
    };

    private static GameDefinition InstallingGame(string appId, string name) => new()
    {
        Id = $"steam-{appId}",
        Name = name,
        Source = new GameSource { Type = "steam", AppId = appId, InstallationPending = true }
    };

    private static PendingGameDetection InstallingSteamDetection(string appId, string name) => new()
    {
        Key = $"steam:{appId}",
        Name = name,
        SourceType = "steam",
        AppId = appId,
        InstallState = InstallState.Installing
    };

    private static PendingGameDetection SteamDetection(string appId, string name, string process) => new()
    {
        Key = $"steam:{appId}",
        Name = name,
        Processes = [process],
        SourceType = "steam",
        AppId = appId
    };
}
