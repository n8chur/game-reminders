namespace GameReminders.App.Tests;

public sealed class LaunchAtLoginServiceTests
{
    [Fact]
    public void EnableRegistersCurrentExecutablePerUser()
    {
        string? registered = null;
        var service = new LaunchAtLoginService(
            @"C:\Apps\GameReminders.exe",
            () => registered,
            value => registered = value,
            () => registered = null);

        Assert.True(service.TrySetEnabled(true, out var error));
        Assert.Null(error);
        Assert.Equal("\"C:\\Apps\\GameReminders.exe\"", registered);
        Assert.True(service.TryGetEnabled(out var enabled, out error));
        Assert.True(enabled);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("\"C:\\Apps\\GameReminders.exe\"")]
    [InlineData(@"C:\Apps\GameReminders.exe")]
    public void EquivalentQuotedOrUnquotedRegistrationIsEnabled(string registered)
    {
        var service = new LaunchAtLoginService(
            @"C:\Apps\GameReminders.exe",
            () => registered,
            _ => { },
            () => { });

        Assert.True(service.TryGetEnabled(out var enabled, out var error));

        Assert.True(enabled);
        Assert.Null(error);
    }

    [Fact]
    public void DisableRemovesRegistration()
    {
        string? registered = "stale";
        var service = new LaunchAtLoginService(
            @"C:\Apps\GameReminders.exe",
            () => registered,
            value => registered = value,
            () => registered = null);

        Assert.True(service.TrySetEnabled(false, out var error));

        Assert.Null(error);
        Assert.Null(registered);
    }

    [Fact]
    public void RegistrationFailureIsVisible()
    {
        var service = new LaunchAtLoginService(
            @"C:\Apps\GameReminders.exe",
            () => null,
            _ => throw new UnauthorizedAccessException("blocked"),
            () => { });

        Assert.False(service.TrySetEnabled(true, out var error));

        Assert.NotNull(error);
        Assert.Contains("could not be enabled", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocked", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupCommitEnablesStartupAndSavesValidatedRoot()
    {
        var startup = new FakeStartup();
        AppSettings? saved = null;
        var current = new AppSettings();

        var result = SetupCommitter.Commit(
            current,
            @"C:\iCloud Drive\Shortcuts",
            launchAtLogin: true,
            _ => StoreRootValidation.Valid(@"C:\iCloud Drive\Shortcuts\Game Reminders"),
            startup,
            settings =>
            {
                saved = settings;
                return true;
            });

        Assert.True(result.Succeeded, result.Error);
        Assert.True(startup.Enabled);
        Assert.Equal(@"C:\iCloud Drive\Shortcuts\Game Reminders", saved?.ICloudRoot);
    }

    [Fact]
    public void StartupFailurePreservesSettingsAndDoesNotSavePreference()
    {
        var current = new AppSettings { ICloudRoot = @"C:\old" };
        var startup = new FakeStartup { SetError = "registry blocked" };
        var saveCalled = false;

        var result = SetupCommitter.Commit(
            current,
            @"C:\new",
            launchAtLogin: true,
            path => StoreRootValidation.Valid(path!),
            startup,
            _ =>
            {
                saveCalled = true;
                return true;
            });

        Assert.False(result.Succeeded);
        Assert.Same(current, result.Settings);
        Assert.False(saveCalled);
        Assert.NotNull(result.Error);
        Assert.Contains("registry blocked", result.Error);
    }

    [Fact]
    public void SettingsFailureRollsBackStartupRegistration()
    {
        var current = new AppSettings { ICloudRoot = @"C:\old" };
        var startup = new FakeStartup();

        var result = SetupCommitter.Commit(
            current,
            @"C:\new",
            launchAtLogin: true,
            path => StoreRootValidation.Valid(path!),
            startup,
            _ => false);

        Assert.False(result.Succeeded);
        Assert.False(startup.Enabled);
        Assert.Equal([true, false], startup.SetRequests);
        Assert.Same(current, result.Settings);
    }

    [Fact]
    public void SettingsExceptionRollsBackStartupRegistration()
    {
        var current = new AppSettings { ICloudRoot = @"C:\old" };
        var startup = new FakeStartup();

        var result = SetupCommitter.Commit(
            current,
            @"C:\new",
            launchAtLogin: true,
            path => StoreRootValidation.Valid(path!),
            startup,
            _ => throw new System.Security.SecurityException("settings blocked"));

        Assert.False(result.Succeeded);
        Assert.False(startup.Enabled);
        Assert.Equal([true, false], startup.SetRequests);
        Assert.Same(current, result.Settings);
        Assert.Contains("settings blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnavailableStartupStatusDoesNotBlockFolderSetupWhenOptInIsOff()
    {
        var current = new AppSettings();
        var startup = new UnreadableStartup();
        AppSettings? saved = null;

        var result = SetupCommitter.Commit(
            current,
            @"C:\iCloud Drive\Shortcuts",
            launchAtLogin: false,
            _ => StoreRootValidation.Valid(@"C:\iCloud Drive\Shortcuts\Game Reminders"),
            startup,
            settings =>
            {
                saved = settings;
                return true;
            });

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(@"C:\iCloud Drive\Shortcuts\Game Reminders", saved?.ICloudRoot);
        Assert.False(startup.SetCalled);
    }

    private sealed class FakeStartup : ILaunchAtLoginService
    {
        public bool Enabled { get; private set; }
        public string? SetError { get; init; }
        public List<bool> SetRequests { get; } = [];

        public bool TryGetEnabled(out bool enabled, out string? error)
        {
            enabled = Enabled;
            error = null;
            return true;
        }

        public bool TrySetEnabled(bool enabled, out string? error)
        {
            SetRequests.Add(enabled);
            if (SetError is not null)
            {
                error = SetError;
                return false;
            }

            Enabled = enabled;
            error = null;
            return true;
        }
    }

    private sealed class UnreadableStartup : ILaunchAtLoginService
    {
        public bool SetCalled { get; private set; }

        public bool TryGetEnabled(out bool enabled, out string? error)
        {
            enabled = false;
            error = "registry unavailable";
            return false;
        }

        public bool TrySetEnabled(bool enabled, out string? error)
        {
            SetCalled = true;
            error = null;
            return true;
        }
    }
}
