using System.ComponentModel;

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
    public async Task DisposeWaitsForInFlightTimerScan()
    {
        using var scanStarted = new ManualResetEventSlim();
        using var releaseScan = new ManualResetEventSlim();
        var monitor = new ProcessLaunchMonitor([], () =>
        {
            scanStarted.Set();
            releaseScan.Wait();
            return [];
        });

        monitor.Start();
        Assert.True(scanStarted.Wait(TimeSpan.FromSeconds(5)));

        var disposeTask = Task.Run(monitor.Dispose);
        await Task.Delay(100);
        Assert.False(disposeTask.IsCompleted);

        releaseScan.Set();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
