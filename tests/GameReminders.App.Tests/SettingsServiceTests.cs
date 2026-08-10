namespace GameReminders.App.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"GameRemindersTests-{Guid.NewGuid():N}");

    [Fact]
    public void SaveIgnoresRetryableFilesystemFailure()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(settingsPath);
        var service = new SettingsService(settingsPath);

        service.Save(new AppSettings { ICloudRoot = @"C:\iCloudDrive\Game Reminders" });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
