namespace GameReminders.App.Tests;

public sealed class TrayDispatcherTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void DispatchIsSuppressedDuringShutdown(bool started, bool finished, bool expected)
    {
        Assert.Equal(expected, TrayDispatcher.ShouldDispatch(started, finished));
    }
}
