using GameReminders.Core;

namespace GameReminders.App;

/// <summary>
/// Polls Steam for games the catalog records as still installing so their entries
/// resolve themselves once the download finishes. The timer only runs while at least
/// one game is pending, and each poll re-reads just those app manifests.
/// </summary>
internal sealed class SteamInstallWatcher : IDisposable
{
    // An install finishing is not time critical, but the user may be waiting to play.
    // This is frequent enough to feel automatic without re-walking a Steam library.
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);
    private readonly Action<IReadOnlySet<string>> _rescan;
    private readonly TimeSpan _pollInterval;
    private readonly System.Threading.Timer _timer;
    private readonly object _lifecycleGate = new();
    private IReadOnlySet<string> _pendingAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool _polling;
    private bool _disposed;

    public SteamInstallWatcher(Action<IReadOnlySet<string>> rescan)
        : this(rescan, DefaultPollInterval)
    {
    }

    internal SteamInstallWatcher(Action<IReadOnlySet<string>> rescan, TimeSpan pollInterval)
    {
        _rescan = rescan;
        _pollInterval = pollInterval;
        _timer = new System.Threading.Timer(Poll, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts polling while the catalog has installing games, and stops when it does not.</summary>
    public void Update(GameCatalog catalog)
    {
        var pending = PendingAppIds(catalog);
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            var wasPolling = _polling;
            _pendingAppIds = pending;
            _polling = pending.Count > 0;
            if (_polling && !wasPolling)
            {
                _timer.Change(_pollInterval, _pollInterval);
            }
            else if (!_polling && wasPolling)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    internal bool IsPolling
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _polling;
            }
        }
    }

    internal void PollOnce() => Poll(null);

    private void Poll(object? state)
    {
        IReadOnlySet<string> pending;
        lock (_lifecycleGate)
        {
            if (_disposed || _pendingAppIds.Count == 0)
            {
                return;
            }

            pending = _pendingAppIds;
        }

        _rescan(pending);
    }

    internal static IReadOnlySet<string> PendingAppIds(GameCatalog catalog) =>
        (catalog.Games ?? [])
            .Where(game => game.Source is { InstallState: InstallState.Installing } source &&
                string.Equals(source.Type?.Trim(), "steam", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(source.AppId))
            .Select(game => game.Source!.AppId!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _polling = false;
        }

        _timer.Dispose();
    }
}
