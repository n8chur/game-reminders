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

    public StoreRootValidator()
        : this(Directory.Exists, File.Exists, File.ReadAllText, VerifyWritable)
    {
    }

    internal StoreRootValidator(
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, string> readAllText,
        Action<string> verifyWritable)
    {
        _directoryExists = directoryExists;
        _fileExists = fileExists;
        _readAllText = readAllText;
        _verifyWritable = verifyWritable;
    }

    public StoreRootValidation ValidateSelection(string? path) => Validate(path, requireCatalog: false);

    public StoreRootValidation ValidateSavedRoot(string? path) => Validate(path, requireCatalog: true);

    private StoreRootValidation Validate(string? path, bool requireCatalog)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return StoreRootValidation.Invalid("Select the existing iCloud Drive/Shortcuts/Game Reminders folder.");
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or ArgumentException)
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
