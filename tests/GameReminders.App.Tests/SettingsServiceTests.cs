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

        service.Save(new AppSettings
        {
            PendingDetections = [detection],
            IgnoredDetectionKeys = ["process:ignored"],
            SuppressedSteamGames = [new SuppressedSteamGame { AppId = "456", Name = "Removed" }],
            UnreviewedGameIds = ["steam-123"]
        });
        var result = service.Load();

        Assert.Equal("steam:123", Assert.Single(result.PendingDetections).Key);
        Assert.Equal("process:ignored", Assert.Single(result.IgnoredDetectionKeys));
        Assert.Equal("456", Assert.Single(result.SuppressedSteamGames).AppId);
        Assert.Equal("steam-123", Assert.Single(result.UnreviewedGameIds));
    }

    [Fact]
    public void NullLocalCollectionsAreRecoveredAsEmpty()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, "{ \"pendingDetections\": null, \"ignoredDetectionKeys\": null, \"suppressedSteamGames\": null, \"unreviewedGameIds\": null }");

        var result = new SettingsService(settingsPath).Load();

        Assert.Empty(result.PendingDetections);
        Assert.Empty(result.IgnoredDetectionKeys);
        Assert.Empty(result.SuppressedSteamGames);
        Assert.Empty(result.UnreviewedGameIds);
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

    [Fact]
    public void MalformedSettingsArePreservedAndReported()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        const string malformed = "{ not-json }";
        File.WriteAllText(settingsPath, malformed);

        var exception = Assert.Throws<InvalidDataException>(() => new SettingsService(settingsPath).Load());

        Assert.Contains("malformed", exception.Message);
        Assert.Equal(malformed, File.ReadAllText(settingsPath));
    }

    [Fact]
    public void DuplicatePendingDetectionsMergeUsefulMetadata()
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "pendingDetections": [
                {
                  "key": "steam:123",
                  "name": "Test Game",
                  "processes": ["Launcher.exe"],
                  "sourceType": "steam",
                  "detectedAt": "2026-08-10T01:00:00Z"
                },
                {
                  "key": "STEAM:123",
                  "name": "Test Game",
                  "processes": ["Game.exe"],
                  "sourceType": "steam",
                  "appId": "123",
                  "detectedAt": "2026-08-10T02:00:00Z"
                }
              ]
            }
            """);

        var detection = Assert.Single(new SettingsService(settingsPath).Load().PendingDetections);

        Assert.Equal("123", detection.AppId);
        Assert.Equal(["Launcher.exe", "Game.exe"], detection.Processes);
        Assert.Equal(DateTimeOffset.Parse("2026-08-10T01:00:00Z"), detection.DetectedAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
