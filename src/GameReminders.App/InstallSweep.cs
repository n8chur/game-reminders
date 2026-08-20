namespace GameReminders.App;

/// <summary>
/// Re-probes install state on a long interval so a game installed or uninstalled while the
/// client sits in the notification area is noticed without waiting for the next start.
/// </summary>
/// <remarks>
/// A tick is skipped while a monitored game is running: the probe is disk work the player
/// did not ask for, and the next tick after they stop playing still catches up. Whether a
/// game is running comes from <see cref="ProcessLaunchMonitor"/>, which already tracks it,
/// rather than from Windows Game Mode, whose preference keys only record whether Game Mode
/// may engage and not whether it currently has.
/// </remarks>
internal sealed class InstallSweep : IDisposable
{
    // Long enough that a tray-resident client costs nothing measurable, short enough that
    // "I just installed it" is answered without restarting the app.
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(10);
    private readonly Action _sweep;
    private readonly Func<bool> _isGameRunning;
    private readonly TimeSpan _interval;
    private readonly System.Threading.Timer _timer;
    private readonly object _lifecycleGate = new();
    private bool _disposed;

    public InstallSweep(Action sweep, Func<bool> isGameRunning)
        : this(sweep, isGameRunning, DefaultInterval)
    {
    }

    internal InstallSweep(Action sweep, Func<bool> isGameRunning, TimeSpan interval)
    {
        _sweep = sweep;
        _isGameRunning = isGameRunning;
        _interval = interval;
        _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Sweeps one interval from now; startup has already scanned.</summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _timer.Change(_interval, _interval);
        }
    }

    internal void TickOnce() => Tick(null);

    internal static bool ShouldSweep(bool gameRunning) => !gameRunning;

    private void Tick(object? state)
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }
        }

        if (ShouldSweep(_isGameRunning()))
        {
            _sweep();
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }
}
