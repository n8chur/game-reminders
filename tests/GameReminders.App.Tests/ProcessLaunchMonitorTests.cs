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
}
