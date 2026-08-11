using System.ComponentModel;
using System.Diagnostics;
using GameReminders.Core;

namespace GameReminders.App.Tests;

public sealed class ProcessLaunchMonitorTests
{
    [Theory]
    [MemberData(nameof(ProcessEnumerationFailures))]
    public void ScanSkipsRetryableProcessEnumerationFailure(Exception failure)
    {
        using var monitor = new ProcessLaunchMonitor([], () => throw failure);

        monitor.ScanOnce();
    }

    public static TheoryData<Exception> ProcessEnumerationFailures => new()
    {
        new InvalidOperationException("Process list changed."),
        new Win32Exception("Process enumeration failed."),
        new UnauthorizedAccessException("Process enumeration was denied.")
    };

    [Fact]
    public async Task DisposePreventsInFlightScanFromRaisingLaunchAfterReturn()
    {
        using var scanStarted = new ManualResetEventSlim();
        using var releaseScan = new ManualResetEventSlim();
        using var currentProcess = Process.GetCurrentProcess();
        var game = new GameDefinition
        {
            Id = "test-game",
            Name = "Test Game",
            Processes = [currentProcess.ProcessName]
        };
        var monitor = new ProcessLaunchMonitor([game], () =>
        {
            scanStarted.Set();
            releaseScan.Wait();
            return [Process.GetCurrentProcess()];
        });
        var launches = 0;
        monitor.GameLaunched += (_, _) => Interlocked.Increment(ref launches);

        var scanTask = Task.Run(monitor.ScanOnce);
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(5)));

        monitor.Dispose();
        releaseScan.Set();
        await scanTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, launches);
    }

    [Fact]
    public void ReplacementMonitorDoesNotRepeatAnAlreadyActiveLaunch()
    {
        using var current = Process.GetCurrentProcess();
        var game = new GameDefinition
        {
            Id = "test-game",
            Name = "Test Game",
            Processes = [current.ProcessName]
        };
        using var original = new ProcessLaunchMonitor([game], Process.GetProcesses);
        original.ScanOnce();

        using var replacement = App.CreateReplacementMonitor(
            [game],
            original,
            Process.GetProcesses);
        var replacementLaunches = 0;
        replacement.GameLaunched += (_, _) => replacementLaunches++;

        replacement.ScanOnce();

        Assert.Equal(0, replacementLaunches);
    }

    [Fact]
    public void FullPathDistinguishesGamesWithTheSameExecutableFilename()
    {
        using var current = Process.GetCurrentProcess();
        var currentPath = current.MainModule!.FileName;
        var filename = Path.GetFileName(currentPath);
        var expected = new GameDefinition
        {
            Id = "expected",
            Name = "Expected",
            Processes = [currentPath]
        };
        var other = new GameDefinition
        {
            Id = "other",
            Name = "Other",
            Processes = [Path.Combine(@"C:\OtherGame", filename)]
        };
        using var monitor = new ProcessLaunchMonitor([expected, other], () => [Process.GetCurrentProcess()]);
        GameDefinition? launched = null;
        monitor.GameLaunched += (_, game) => launched = game;

        monitor.ScanOnce();

        Assert.Equal("expected", launched?.Id);
    }

    [Fact]
    public void ConfiguredSteamRelativePathSuppressesMatchingAbsoluteForegroundDetection()
    {
        var catalog = new GameCatalog
        {
            Games =
            [
                new GameDefinition
                {
                    Id = "steam-123",
                    Name = "Test Game",
                    Processes = [@"Test Game\Binaries\Win64\TestGame.exe"],
                    Source = new GameSource { Type = "steam", AppId = "123" }
                }
            ]
        };
        var detection = new PendingGameDetection
        {
            Key = "process:testgame",
            Name = "Test Game",
            Processes = [@"D:\SteamLibrary\steamapps\common\Test Game\Binaries\Win64\TestGame.exe"],
            SourceType = "detected"
        };

        Assert.True(App.IsConfiguredDetection(catalog, detection));
    }

    [Fact]
    public void RestoringIgnoredDetectionReturnsRetainedMetadataToPending()
    {
        var retained = new PendingGameDetection
        {
            Key = "process:testgame",
            Name = "Test Game",
            Processes = [@"D:\Games\TestGame.exe"],
            SourceType = "detected"
        };
        var settings = new AppSettings
        {
            PendingDetections =
            [
                new PendingGameDetection
                {
                    Key = "process:other",
                    Name = "Other",
                    Processes = ["Other.exe"],
                    SourceType = "detected"
                }
            ],
            IgnoredDetections = [retained],
            IgnoredDetectionKeys = ["PROCESS:TESTGAME"]
        };

        var restored = App.RestoreIgnoredDetection(settings, "process:testgame");

        Assert.Equal(2, restored.PendingDetections.Count);
        Assert.Contains(retained, restored.PendingDetections);
        Assert.Empty(restored.IgnoredDetections);
        Assert.Empty(restored.IgnoredDetectionKeys);
    }
}
