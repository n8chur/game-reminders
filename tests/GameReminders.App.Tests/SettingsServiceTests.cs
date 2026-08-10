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
        Assert.False(service.TrySave(new AppSettings()));
    }

    [Fact]
    public void PendingDetectionsRoundTrip()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        var service = new SettingsService(settingsPath);
        var detection = new PendingGameDetection
        {
            Key = "steam:123",
            Name = "Test Game",
            Processes = ["TestGame.exe"],
            SourceType = "steam",
            AppId = "123"
        };

        service.Save(new AppSettings { PendingDetections = [detection], IgnoredDetectionKeys = ["process:ignored"] });
        var result = service.Load();

        Assert.Equal("steam:123", Assert.Single(result.PendingDetections).Key);
        Assert.Equal("process:ignored", Assert.Single(result.IgnoredDetectionKeys));
    }

    [Fact]
    public void NullLocalCollectionsAreRecoveredAsEmpty()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, "{ \"pendingDetections\": null, \"ignoredDetectionKeys\": null }");

        var result = new SettingsService(settingsPath).Load();

        Assert.Empty(result.PendingDetections);
        Assert.Empty(result.IgnoredDetectionKeys);
    }

    [Fact]
    public void InvalidPendingProcessEntriesAreRemoved()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "pendingDetections": [
                {
                  "key": "steam:123",
                  "name": "Test Game",
                  "processes": [null, "", "  ", ".exe", "TestGame.exe", "testgame.exe"],
                  "sourceType": "steam"
                }
              ]
            }
            """);

        var detection = Assert.Single(new SettingsService(settingsPath).Load().PendingDetections);

        Assert.Equal("TestGame.exe", Assert.Single(detection.Processes));
    }

    [Fact]
    public void IncompletePendingDetectionDoesNotDiscardOtherSettings()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "iCloudRoot": "C:\\iCloudDrive\\Game Reminders",
              "pendingDetections": [
                { "key": "steam:123", "name": "Incomplete" }
              ]
            }
            """);

        var result = new SettingsService(settingsPath).Load();

        Assert.Equal(@"C:\iCloudDrive\Game Reminders", result.ICloudRoot);
        Assert.Empty(result.PendingDetections);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
