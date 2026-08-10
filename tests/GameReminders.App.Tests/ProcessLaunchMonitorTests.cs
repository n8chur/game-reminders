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
}
