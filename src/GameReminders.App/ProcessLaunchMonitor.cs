using System.Diagnostics;
using GameReminders.Core;

namespace GameReminders.App;

public sealed class ProcessLaunchMonitor : IDisposable
{
    // Polling is the Milestone 1 prototype's primary launch detector, so it must
    // remain responsive. A 60-second fallback replaces this cadence once the
    // later event-driven process monitor is implemented.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, GameDefinition> _gamesByProcess;
    private readonly System.Threading.Timer _timer;
    private readonly HashSet<string> _activeGameIds = new(StringComparer.OrdinalIgnoreCase);
    private int _scanInProgress;

    public ProcessLaunchMonitor(IReadOnlyList<GameDefinition> games)
    {
        _gamesByProcess = games
            .SelectMany(game => game.Processes.Select(process => (Process: NameNormalizer.NormalizeProcessName(process), Game: game)))
            .GroupBy(item => item.Process, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.Game)
                    .GroupBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
                    .Single()
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        _timer = new System.Threading.Timer(Scan, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<GameDefinition>? GameLaunched;

    public void Start()
    {
        // Scan immediately so a configured game that was already running when
        // the client started still triggers its pending reminders.
        _timer.Change(TimeSpan.Zero, ScanInterval);
    }

    private void Scan(object? state)
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var running = FindRunningGames().ToDictionary(game => game.Id, StringComparer.OrdinalIgnoreCase);
            var launched = running.Values.Where(game => !_activeGameIds.Contains(game.Id)).ToArray();

            _activeGameIds.Clear();
            _activeGameIds.UnionWith(running.Keys);

            foreach (var game in launched)
            {
                GameLaunched?.Invoke(this, game);
            }
        }
        finally
        {
            Volatile.Write(ref _scanInProgress, 0);
        }
    }

    private IEnumerable<GameDefinition> FindRunningGames()
    {
        var found = new Dictionary<string, GameDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var processName = NameNormalizer.NormalizeProcessName(process.ProcessName);
                if (_gamesByProcess.TryGetValue(processName, out var game))
                {
                    found.TryAdd(game.Id, game);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while it was being inspected.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Windows can deny metadata access for protected processes.
            }
            catch (UnauthorizedAccessException)
            {
                // Skip processes whose metadata cannot be read at this privilege level.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found.Values;
    }

    public void Dispose() => _timer.Dispose();
}
