using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class StoreRootValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"GameRemindersSetupTests-{Guid.NewGuid():N}");

    [Fact]
    public void FreshSettingsRequireFirstRunSetup()
    {
        var state = SetupStateResolver.Resolve(new AppSettings(), _ => throw new InvalidOperationException());

        Assert.Equal(SetupRequirement.FirstRun, state.Requirement);
        Assert.Null(state.Root);
    }

    [Fact]
    public void ExistingValidConfigurationStartsWithoutSetup()
    {
        var settings = new AppSettings { ICloudRoot = @"C:\iCloud Drive\Shortcuts\Game Reminders" };

        var state = SetupStateResolver.Resolve(
            settings,
            path => StoreRootValidation.Valid(path!));

        Assert.Equal(SetupRequirement.None, state.Requirement);
        Assert.Equal(settings.ICloudRoot, state.Root);
    }

    [Fact]
    public void InvalidSavedFolderRequiresRecoveryWithoutChangingSavedValue()
    {
        var settings = new AppSettings { ICloudRoot = @"C:\missing\Game Reminders" };

        var state = SetupStateResolver.Resolve(
            settings,
            _ => StoreRootValidation.Invalid("Folder unavailable"));

        Assert.Equal(SetupRequirement.RecoverFolder, state.Requirement);
        Assert.Equal(settings.ICloudRoot, state.SavedRoot);
        Assert.Equal("Folder unavailable", state.Error);
        Assert.Equal(@"C:\missing\Game Reminders", settings.ICloudRoot);
    }

    [Fact]
    public void ExistingStoreIsValidatedWithoutChangingCatalogOrReminders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "inbox"));
        var catalogPath = Path.Combine(_root, "games.json");
        var reminderPath = Path.Combine(_root, "inbox", "pending.json");
        var catalog = JsonProtocol.WriteCatalog(new GameCatalog
        {
            Games = [new GameDefinition { Id = "farever", Name = "Farever", Processes = ["Farever.exe"] }]
        });
        File.WriteAllText(catalogPath, catalog);
        File.WriteAllText(reminderPath, "pending reminder marker");

        var result = new StoreRootValidator().ValidateSavedRoot(_root);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Path.GetFullPath(_root), result.Root);
        Assert.Equal(catalog, File.ReadAllText(catalogPath));
        Assert.Equal("pending reminder marker", File.ReadAllText(reminderPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(_root), path =>
            Path.GetFileName(path).StartsWith(".game-reminders-write-test-", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedExistingCatalogIsRejected()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "games.json"), "{ not-json }");

        var result = new StoreRootValidator().ValidateSavedRoot(_root);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
        Assert.Contains("cannot use", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableFolderIsRejectedBeforeWriteProbe()
    {
        var probed = false;
        var validator = new StoreRootValidator(
            _ => false,
            _ => false,
            _ => throw new InvalidOperationException(),
            _ => probed = true);

        var result = validator.ValidateSavedRoot(_root);

        Assert.False(result.IsValid);
        Assert.False(probed);
    }

    [Fact]
    public void SavedFolderWithoutCatalogRequiresExplicitRecovery()
    {
        Directory.CreateDirectory(_root);
        var validator = new StoreRootValidator();

        var savedResult = validator.ValidateSavedRoot(_root);
        var deliberateSelection = validator.ValidateSelection(_root);

        Assert.False(savedResult.IsValid);
        Assert.Contains("games.json", savedResult.Error!);
        Assert.True(deliberateSelection.IsValid, deliberateSelection.Error);
        Assert.False(File.Exists(Path.Combine(_root, "games.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
