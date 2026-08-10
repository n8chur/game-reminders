namespace GameReminders.App.Tests;

public sealed class ForegroundGameDetectorTests
{
    [Fact]
    public async Task ScanDoesNotOverlap()
    {
        using var scanStarted = new ManualResetEventSlim();
        using var releaseScan = new ManualResetEventSlim();
        var calls = 0;
        using var detector = new ForegroundGameDetector(() =>
        {
            Interlocked.Increment(ref calls);
            scanStarted.Set();
            releaseScan.Wait();
            return null;
        });

        var firstScan = Task.Run(detector.ScanOnce);
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(5)));

        detector.ScanOnce();
        releaseScan.Set();
        await firstScan.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DisposePreventsInFlightScanFromRaisingDetectionAfterReturn()
    {
        using var scanStarted = new ManualResetEventSlim();
        using var releaseScan = new ManualResetEventSlim();
        var detection = new PendingGameDetection
        {
            Key = "process:testgame",
            Name = "Test Game",
            Processes = ["TestGame.exe"],
            SourceType = "detected"
        };
        var calls = 0;
        var detector = new ForegroundGameDetector(() =>
        {
            if (Interlocked.Increment(ref calls) == 3)
            {
                scanStarted.Set();
                releaseScan.Wait();
            }
            return detection;
        });
        var detections = 0;
        detector.GameDetected += (_, _) => Interlocked.Increment(ref detections);

        detector.ScanOnce();
        detector.ScanOnce();
        var thirdScan = Task.Factory.StartNew(
            detector.ScanOnce,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(5)));

        detector.Dispose();
        releaseScan.Set();
        await thirdScan.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, detections);
    }
}
