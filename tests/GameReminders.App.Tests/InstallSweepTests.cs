namespace GameReminders.App.Tests;

public sealed class InstallSweepTests
{
    [Fact]
    public void SweepIsSkippedWhileAMonitoredGameIsRunning()
    {
        Assert.True(InstallSweep.ShouldSweep(gameRunning: false));
        Assert.False(InstallSweep.ShouldSweep(gameRunning: true));
    }

    [Fact]
    public void ATickRunsOnlyWhenNoGameIsRunning()
    {
        var sweeps = 0;
        var gameRunning = true;
        using var sweep = new InstallSweep(() => sweeps++, () => gameRunning, TimeSpan.FromMinutes(30));

        sweep.TickOnce();
        Assert.Equal(0, sweeps);

        gameRunning = false;
        sweep.TickOnce();
        Assert.Equal(1, sweeps);
    }

    [Fact]
    public void DisposedSweepStopsRunning()
    {
        var sweeps = 0;
        var sweep = new InstallSweep(() => sweeps++, () => false, TimeSpan.FromMinutes(30));

        sweep.Start();
        sweep.Dispose();
        sweep.TickOnce();

        Assert.Equal(0, sweeps);
    }
}
