using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class InstallVerificationTests
{
    private static readonly Func<string, bool> AllVolumesPresent = _ => true;

    [Fact]
    public void MissingExecutableFlagsAManualGameWithoutTouchingItsMapping()
    {
        var game = Game("custom-1", "manual", @"C:\Games\Everwind\Everwind.exe");

        var verified = InstallVerification.Verify(game, _ => false, AllVolumesPresent);

        Assert.Equal(InstallState.NotInstalled, verified.Source?.InstallState);
        Assert.Equal(@"C:\Games\Everwind\Everwind.exe", Assert.Single(verified.Processes));
    }

    [Fact]
    public void ReturningExecutableClearsTheFlag()
    {
        var game = Game("custom-1", "detected", @"C:\Games\Everwind\Everwind.exe") with
        {
            Source = new GameSource
            {
                Type = "detected",
                InstallState = InstallState.NotInstalled
            }
        };

        var verified = InstallVerification.Verify(game, _ => true, AllVolumesPresent);

        Assert.Equal(InstallState.Installed, verified.Source?.InstallState);
    }

    [Fact]
    public void FilenameOnlyMappingHasNothingToCheckAndStaysInstalled()
    {
        var game = Game("custom-1", "manual", "Farever.exe");

        Assert.Same(game, InstallVerification.Verify(game, _ => false, AllVolumesPresent));
    }

    [Fact]
    public void DisconnectedVolumeIsNotAnUninstall()
    {
        var game = Game("custom-1", "manual", @"E:\Games\Everwind\Everwind.exe");

        Assert.Same(game, InstallVerification.Verify(game, _ => false, _ => false));
    }

    [Fact]
    public void OneSurvivingExecutableKeepsAMultiMappedGameInstalled()
    {
        var game = Game("custom-1", "manual", @"C:\Games\Everwind\Launcher.exe", @"C:\Games\Everwind\Everwind.exe");

        var verified = InstallVerification.Verify(
            game,
            path => path.EndsWith(@"\Everwind.exe", StringComparison.OrdinalIgnoreCase),
            AllVolumesPresent);

        Assert.Equal(InstallState.Installed, verified.Source?.InstallState);
    }

    [Fact]
    public void AMultiMappedGameIsFlaggedOnlyWhenEveryCheckableMappingIsGone()
    {
        var game = Game("custom-1", "manual", @"C:\Games\Everwind\Launcher.exe", @"C:\Games\Everwind\Everwind.exe");

        var verified = InstallVerification.Verify(game, _ => false, AllVolumesPresent);

        Assert.Equal(InstallState.NotInstalled, verified.Source?.InstallState);
    }

    [Fact]
    public void SteamGamesAreLeftToTheSteamImporter()
    {
        var game = Game("steam-123", "steam", @"Everwind\Everwind.exe") with
        {
            Source = new GameSource { Type = "steam", AppId = "123" }
        };

        Assert.Same(game, InstallVerification.Verify(game, _ => false, AllVolumesPresent));
    }

    [Fact]
    public void AGameWithNoMappingAtAllIsLeftAlone()
    {
        var game = new GameDefinition
        {
            Id = "custom-1",
            Name = "Everwind",
            Source = new GameSource { Type = "manual", RequiresExecutableReview = true }
        };

        Assert.Same(game, InstallVerification.Verify(game, _ => false, AllVolumesPresent));
    }

    [Fact]
    public void ASourcelessEntryGainsAManualSourceOnlyWhenItIsActuallyFlagged()
    {
        var game = new GameDefinition
        {
            Id = "custom-1",
            Name = "Everwind",
            Processes = [@"C:\Games\Everwind\Everwind.exe"]
        };

        Assert.Same(game, InstallVerification.Verify(game, _ => true, AllVolumesPresent));

        var flagged = InstallVerification.Verify(game, _ => false, AllVolumesPresent);

        Assert.Equal("manual", flagged.Source?.Type);
        Assert.Equal(InstallState.NotInstalled, flagged.Source?.InstallState);
    }

    [Fact]
    public void CatalogVerificationReportsOnlyTheEntriesThatMoved()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                Game("custom-1", "manual", @"C:\Games\Gone\Gone.exe"),
                Game("custom-2", "manual", @"C:\Games\Here\Here.exe"),
                Game("steam-123", "steam", @"Everwind\Everwind.exe")
            ]
        };

        var result = InstallVerification.Verify(
            catalog,
            path => path.Contains(@"\Here\", StringComparison.OrdinalIgnoreCase),
            AllVolumesPresent);

        var changed = Assert.Single(result.ChangedGames);
        Assert.Equal("custom-1", changed.Id);
        Assert.Equal(InstallState.NotInstalled, changed.Source?.InstallState);
        Assert.Equal(3, result.Catalog.Games.Count);
        Assert.Equal(
            InstallState.Installed,
            result.Catalog.Games.Single(game => game.Id == "custom-2").Source?.InstallState);
    }

    [Fact]
    public void AnUnchangedCatalogIsReturnedUntouched()
    {
        var catalog = new GameCatalog { Games = [Game("custom-1", "manual", "Farever.exe")] };

        var result = InstallVerification.Verify(catalog, _ => false, AllVolumesPresent);

        Assert.Same(catalog, result.Catalog);
        Assert.Empty(result.ChangedGames);
    }

    private static GameDefinition Game(string id, string sourceType, params string[] processes) => new()
    {
        Id = id,
        Name = id,
        Processes = processes,
        Source = new GameSource { Type = sourceType }
    };
}
