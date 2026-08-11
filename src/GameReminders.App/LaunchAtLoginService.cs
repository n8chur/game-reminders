using Microsoft.Win32;

namespace GameReminders.App;

internal interface ILaunchAtLoginService
{
    bool TryGetEnabled(out bool enabled, out string? error);
    bool TrySetEnabled(bool enabled, out string? error);
}

internal sealed class LaunchAtLoginService : ILaunchAtLoginService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameReminders";
    private readonly string _command;
    private readonly Func<string?> _readValue;
    private readonly Action<string> _writeValue;
    private readonly Action _deleteValue;

    public LaunchAtLoginService(string executablePath)
        : this(
            executablePath,
            () => Registry.CurrentUser.OpenSubKey(RunKeyPath)?.GetValue(ValueName) as string,
            value =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                    ?? throw new IOException("The Windows startup registry key could not be opened.");
                key.SetValue(ValueName, value, RegistryValueKind.String);
            },
            () =>
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            })
    {
    }

    internal LaunchAtLoginService(
        string executablePath,
        Func<string?> readValue,
        Action<string> writeValue,
        Action deleteValue)
    {
        _command = QuoteExecutable(executablePath);
        _readValue = readValue;
        _writeValue = writeValue;
        _deleteValue = deleteValue;
    }

    public bool TryGetEnabled(out bool enabled, out string? error)
    {
        try
        {
            enabled = string.Equals(_readValue(), _command, StringComparison.OrdinalIgnoreCase);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            enabled = false;
            error = $"Windows launch-at-login status could not be read: {exception.Message}";
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            if (enabled)
            {
                _writeValue(_command);
            }
            else
            {
                _deleteValue();
            }

            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            error = $"Windows launch-at-login could not be {(enabled ? "enabled" : "disabled")}: {exception.Message}";
            return false;
        }
    }

    internal static string QuoteExecutable(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\"";
}

internal sealed record SetupCommitResult(bool Succeeded, AppSettings Settings, string? Error);

internal static class SetupCommitter
{
    public static SetupCommitResult Commit(
        AppSettings current,
        string? selectedRoot,
        bool launchAtLogin,
        Func<string?, StoreRootValidation> validate,
        ILaunchAtLoginService startup,
        Func<AppSettings, bool> saveSettings)
    {
        var validation = validate(selectedRoot);
        if (!validation.IsValid || validation.Root is null)
        {
            return new SetupCommitResult(false, current, validation.Error);
        }

        var startupStatusKnown = startup.TryGetEnabled(out var previousStartup, out var statusError);
        if (!startupStatusKnown && launchAtLogin)
        {
            return new SetupCommitResult(false, current, statusError);
        }

        var changedStartup = startupStatusKnown && previousStartup != launchAtLogin;
        if (changedStartup && !startup.TrySetEnabled(launchAtLogin, out var startupError))
        {
            return new SetupCommitResult(false, current, startupError);
        }

        var updated = current with { ICloudRoot = validation.Root };
        if (saveSettings(updated))
        {
            return new SetupCommitResult(true, updated, null);
        }

        var rollbackError = changedStartup && !startup.TrySetEnabled(previousStartup, out var error)
            ? $" Windows launch-at-login also could not be restored: {error}"
            : string.Empty;
        return new SetupCommitResult(
            false,
            current,
            $"The selected folder could not be saved; the previous configuration was preserved.{rollbackError}");
    }
}
