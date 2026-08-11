using System.Text.Json;
using GameReminders.Core;

namespace GameReminders.App;

internal sealed record StoreRootValidation(bool IsValid, string? Root, string? Error)
{
    public static StoreRootValidation Valid(string root) => new(true, root, null);
    public static StoreRootValidation Invalid(string error) => new(false, null, error);
}

internal sealed class StoreRootValidator
{
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string> _readAllText;
    private readonly Action<string> _verifyWritable;

    private readonly Action<string> _createDirectory;
    private readonly ICloudFolderPinService _pinService;

    public StoreRootValidator()
        : this(
            Directory.Exists,
            File.Exists,
            File.ReadAllText,
            VerifyWritable,
            path => Directory.CreateDirectory(path),
            new CloudFolderPinService())
    {
    }

    internal StoreRootValidator(
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        Action<string> verifyWritable,
        Action<string>? createDirectory = null,
        ICloudFolderPinService? pinService = null)
    {
        _directoryExists = directoryExists;
        _fileExists = fileExists;
        _readAllText = readAllText;
        _verifyWritable = verifyWritable;
        _createDirectory = createDirectory ?? (path => Directory.CreateDirectory(path));
        _pinService = pinService ?? new CloudFolderPinService();
    }

    public StoreRootValidation ValidateShortcutsSelection(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return StoreRootValidation.Invalid("Select the Shortcuts folder in iCloud Drive.");
        }

        try
        {
            var shortcutsRoot = Path.GetFullPath(path.Trim());
            if (!_directoryExists(shortcutsRoot))
            {
                return StoreRootValidation.Invalid(
                    "That folder is unavailable. Wait for iCloud Drive to finish syncing, then select its Shortcuts folder.");
            }

            if (!ShortcutsFolderLocator.IsShortcutsFolder(shortcutsRoot) ||
                !ShortcutsFolderLocator.IsDirectlyInsideICloudDrive(shortcutsRoot))
            {
                return StoreRootValidation.Invalid(
                    "Select the Shortcuts folder inside iCloud Drive. Game Reminders will create or use its required Game Reminders subfolder.");
            }

            var storeRoot = Path.Combine(shortcutsRoot, ShortcutsFolderLocator.StoreFolderName);
            if (!_directoryExists(storeRoot))
            {
                _createDirectory(storeRoot);
            }

            var validation = Validate(storeRoot, requireCatalog: false);
            if (!validation.IsValid || validation.Root is null)
            {
                return validation;
            }

            return _pinService.TryEnsurePinned(validation.Root, out var pinError)
                ? validation
                : StoreRootValidation.Invalid(pinError ?? "Always keep on this device could not be enabled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return StoreRootValidation.Invalid($"Game Reminders cannot use that folder: {exception.Message}");
        }
    }

    public StoreRootValidation ValidateSavedRoot(string? path) => Validate(path, requireCatalog: true);

    private StoreRootValidation Validate(string? path, bool requireCatalog)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return StoreRootValidation.Invalid("Select the Shortcuts folder in iCloud Drive.");
        }

        try
        {
            var root = Path.GetFullPath(path.Trim());
            if (!_directoryExists(root))
            {
                return StoreRootValidation.Invalid(
                    "That folder is unavailable. Wait for iCloud Drive to finish syncing, or select the existing Game Reminders folder.");
            }

            var catalogPath = Path.Combine(root, "games.json");
            if (_fileExists(catalogPath))
            {
                JsonProtocol.ReadCatalog(_readAllText(catalogPath));
            }
            else if (requireCatalog)
            {
                return StoreRootValidation.Invalid(
                    "The saved folder no longer contains games.json. Select the authoritative Game Reminders folder to avoid creating a second store.");
            }

            _verifyWritable(root);
            return StoreRootValidation.Valid(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException or JsonException or NotSupportedException or ArgumentException)
        {
            return StoreRootValidation.Invalid(
                $"Game Reminders cannot use that folder: {exception.Message}");
        }
    }

    private static void VerifyWritable(string root)
    {
        var probe = Path.Combine(root, $".game-reminders-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       probe,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1,
                       FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }
    }

}

internal static class ShortcutsFolderLocator
{
    public const string StoreFolderName = "Game Reminders";
    private const string PhysicalFolderName = "iCloud~is~workflow~my~workflows";

    public static string? Find(string userProfile)
    {
        try
        {
            var candidates = new[] { "iCloudDrive", "iCloud Drive" }
                .Select(name => Path.Combine(userProfile, name))
                .Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories)
                .Where(IsShortcutsFolder)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 1)
            {
                return candidates[0];
            }

            var candidatesWithStore = candidates
                .Where(candidate => Directory.Exists(Path.Combine(candidate, StoreFolderName)))
                .ToArray();
            return candidatesWithStore.Length == 1 ? candidatesWithStore[0] : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static string? FromSavedRoot(string? savedRoot)
    {
        if (string.IsNullOrWhiteSpace(savedRoot))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(savedRoot.Trim());
            var parent = Directory.GetParent(root)?.FullName;
            return string.Equals(Path.GetFileName(root), StoreFolderName, StringComparison.OrdinalIgnoreCase) &&
                   parent is not null &&
                   Directory.Exists(parent) &&
                   IsShortcutsFolder(parent)
                ? parent
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public static bool IsShortcutsFolder(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.Equals(name, "Shortcuts", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, PhysicalFolderName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDirectlyInsideICloudDrive(string path)
    {
        try
        {
            var parentName = Directory.GetParent(Path.GetFullPath(path))?.Name;
            return string.Equals(parentName, "iCloudDrive", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(parentName, "iCloud Drive", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static string ToDisplayPath(string path) =>
        path.Replace(PhysicalFolderName, "Shortcuts", StringComparison.OrdinalIgnoreCase);
}

internal enum SetupRequirement
{
    None,
    FirstRun,
    RecoverFolder
}

internal sealed record SetupState(
    SetupRequirement Requirement,
    string? Root,
    string? SavedRoot,
    string? Error);

internal static class SetupStateResolver
{
    public static SetupState Resolve(
        AppSettings settings,
        Func<string?, StoreRootValidation> validate)
    {
        if (string.IsNullOrWhiteSpace(settings.ICloudRoot))
        {
            return new SetupState(SetupRequirement.FirstRun, null, null, null);
        }

        var validation = validate(settings.ICloudRoot);
        return validation.IsValid
            ? new SetupState(SetupRequirement.None, validation.Root, settings.ICloudRoot, null)
            : new SetupState(SetupRequirement.RecoverFolder, null, settings.ICloudRoot, validation.Error);
    }
}
