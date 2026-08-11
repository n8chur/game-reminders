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
    public void ValidatedRootReplacesUnnormalizedRuntimeSetting()
    {
        var settings = new AppSettings { ICloudRoot = @" C:\iCloud Drive\Shortcuts\Game Reminders " };
        var normalized = @"C:\iCloud Drive\Shortcuts\Game Reminders";
        var state = SetupStateResolver.Resolve(
            settings,
            _ => StoreRootValidation.Valid(normalized));

        var updated = App.ApplyValidatedRoot(settings, state);

        Assert.Equal(normalized, updated.ICloudRoot);
        Assert.Equal(@" C:\iCloud Drive\Shortcuts\Game Reminders ", settings.ICloudRoot);
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
    public void SecurityFailureIsReportedAsInvalid()
    {
        var validator = new StoreRootValidator(
            _ => throw new System.Security.SecurityException("blocked"),
            _ => false,
            _ => throw new InvalidOperationException(),
            _ => { });

        var result = validator.ValidateSavedRoot(_root);

        Assert.False(result.IsValid);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SavedFolderWithoutCatalogRequiresExplicitRecovery()
    {
        Directory.CreateDirectory(_root);
        var validator = new StoreRootValidator();

        var savedResult = validator.ValidateSavedRoot(_root);

        Assert.False(savedResult.IsValid);
        Assert.Contains("games.json", savedResult.Error!);
        Assert.False(File.Exists(Path.Combine(_root, "games.json")));
    }

    [Theory]
    [InlineData("Shortcuts")]
    [InlineData("iCloud~is~workflow~my~workflows")]
    public void ShortcutsSelectionCreatesAndPinsRequiredStore(string shortcutsName)
    {
        var shortcutsRoot = Path.Combine(_root, "iCloudDrive", shortcutsName);
        Directory.CreateDirectory(shortcutsRoot);
        var pin = new FakePinService();
        var validator = CreateSelectionValidator(pin);

        var result = validator.ValidateShortcutsSelection(shortcutsRoot);

        var expected = Path.Combine(shortcutsRoot, "Game Reminders");
        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Path.GetFullPath(expected), result.Root);
        Assert.True(Directory.Exists(expected));
        Assert.Equal(expected, pin.PinnedPath);
    }

    [Fact]
    public void ShortcutsSelectionAcceptsSpacedICloudDriveName()
    {
        var shortcutsRoot = Path.Combine(_root, "iCloud Drive", "Shortcuts");
        Directory.CreateDirectory(shortcutsRoot);
        var result = CreateSelectionValidator(new FakePinService())
            .ValidateShortcutsSelection(shortcutsRoot);

        Assert.True(result.IsValid, result.Error);
        Assert.EndsWith(Path.Combine("Shortcuts", "Game Reminders"), result.Root!);
    }

    [Fact]
    public void SelectingGameRemindersFolderDirectlyIsRejected()
    {
        var storeRoot = Path.Combine(_root, "iCloudDrive", "Shortcuts", "Game Reminders");
        Directory.CreateDirectory(storeRoot);
        var validator = CreateSelectionValidator(new FakePinService());

        var result = validator.ValidateShortcutsSelection(storeRoot);

        Assert.False(result.IsValid);
        Assert.Contains("Shortcuts folder", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FolderOutsideICloudDriveIsRejected()
    {
        var shortcutsRoot = Path.Combine(_root, "Shortcuts");
        Directory.CreateDirectory(shortcutsRoot);
        var validator = CreateSelectionValidator(new FakePinService());

        var result = validator.ValidateShortcutsSelection(shortcutsRoot);

        Assert.False(result.IsValid);
        Assert.Contains("iCloud Drive", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedShortcutsFolderInsideICloudDriveIsRejected()
    {
        var shortcutsRoot = Path.Combine(_root, "iCloudDrive", "Other", "Shortcuts");
        Directory.CreateDirectory(shortcutsRoot);

        var result = CreateSelectionValidator(new FakePinService())
            .ValidateShortcutsSelection(shortcutsRoot);

        Assert.False(result.IsValid);
        Assert.Contains("iCloud Drive", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PinFailureExplainsManualFallback()
    {
        var shortcutsRoot = Path.Combine(_root, "iCloudDrive", "Shortcuts");
        Directory.CreateDirectory(shortcutsRoot);
        var validator = CreateSelectionValidator(new FakePinService("pin blocked"));

        var result = validator.ValidateShortcutsSelection(shortcutsRoot);

        Assert.False(result.IsValid);
        Assert.Equal("pin blocked", result.Error);
    }

    [Fact]
    public void LocatorFindsPhysicalShortcutsContainerAndUsesFriendlyDisplayName()
    {
        var shortcutsRoot = Path.Combine(_root, "iCloudDrive", "iCloud~is~workflow~my~workflows");
        Directory.CreateDirectory(shortcutsRoot);

        var result = ShortcutsFolderLocator.Find(_root);

        Assert.Equal(Path.GetFullPath(shortcutsRoot), result);
        Assert.EndsWith(Path.Combine("iCloudDrive", "Shortcuts"), ShortcutsFolderLocator.ToDisplayPath(result!));
    }

    [Fact]
    public void ExistingPinnedFolderDoesNotRequestPinAgain()
    {
        var setCalled = false;
        var service = new CloudFolderPinService(
            _ => (FileAttributes)0x00080000,
            _ =>
            {
                setCalled = true;
                return 0;
            });

        Assert.True(service.TryEnsurePinned(_root, out var error));
        Assert.Null(error);
        Assert.False(setCalled);
    }

    [Fact]
    public void UnpinnedFolderRequestsRecursivePin()
    {
        var pinnedPath = string.Empty;
        var service = new CloudFolderPinService(
            _ => FileAttributes.Directory,
            path =>
            {
                pinnedPath = path;
                return 0;
            });

        Assert.True(service.TryEnsurePinned(_root, out var error));
        Assert.Null(error);
        Assert.Equal(_root, pinnedPath);
    }

    private static StoreRootValidator CreateSelectionValidator(ICloudFolderPinService pinService) =>
        new(
            Directory.Exists,
            File.Exists,
            File.ReadAllText,
            _ => { },
            path => Directory.CreateDirectory(path),
            pinService);

    private sealed class FakePinService(string? error = null) : ICloudFolderPinService
    {
        public string? PinnedPath { get; private set; }

        public bool TryEnsurePinned(string path, out string? pinError)
        {
            PinnedPath = path;
            pinError = error;
            return error is null;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
