using System.Diagnostics;
using System.ComponentModel;
using GameReminders.Core;

namespace GameReminders.App;

public sealed class ProcessLaunchMonitor : IDisposable
{
    // Polling is the Milestone 1 prototype's primary launch detector, so it must
    // remain responsive. A 60-second fallback replaces this cadence once the
    // later event-driven process monitor is implemented.
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(5);
    private readonly Dictionary<string, GameDefinition> _gamesByProcessName;
    private readonly IReadOnlyList<(string Path, GameDefinition Game)> _gamesByPath;
    private readonly Func<Process[]> _getProcesses;
    private readonly System.Threading.Timer _timer;
    private readonly object _lifecycleGate = new();
    private readonly HashSet<string> _activeGameIds = new(StringComparer.OrdinalIgnoreCase);
    private int _scanInProgress;
    private bool _disposed;

    public ProcessLaunchMonitor(IReadOnlyList<GameDefinition> games)
        : this(games, Process.GetProcesses)
    {
    }

    internal ProcessLaunchMonitor(
        IReadOnlyList<GameDefinition> games,
        Func<Process[]> getProcesses,
        IEnumerable<string>? activeGameIds = null)
    {
        var mappings = games
            .SelectMany(game => game.Processes.Select(process => (Process: process, Game: game)))
            .ToArray();
        _gamesByPath = mappings
            .Where(item => NameNormalizer.IsExecutablePath(item.Process))
            .Select(item => (NameNormalizer.NormalizeExecutableIdentity(item.Process), item.Game))
            .ToArray();
        _gamesByProcessName = mappings
            .Select(item => (Process: NameNormalizer.NormalizeProcessName(item.Process), item.Game))
            .GroupBy(item => item.Process, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.Game.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.First().Game,
                StringComparer.OrdinalIgnoreCase);
        _getProcesses = getProcesses;
        _timer = new System.Threading.Timer(Scan, null, Timeout.Infinite, Timeout.Infinite);
        _activeGameIds.UnionWith(activeGameIds ?? []);
    }

    public event EventHandler<GameDefinition>? GameLaunched;

    public void Start()
    {
        // Scan immediately so a configured game that was already running when
        // the client started still triggers its pending reminders.
        _timer.Change(TimeSpan.Zero, ScanInterval);
    }

    internal void ScanOnce() => Scan(null);

    private void Scan(object? state)
    {
        if (Interlocked.Exchange(ref _scanInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            var running = FindRunningGames().ToDictionary(game => game.Id, StringComparer.OrdinalIgnoreCase);
            GameDefinition[] launched;
            lock (_lifecycleGate)
            {
                if (_disposed)
                {
                    return;
                }

                launched = running.Values.Where(game => !_activeGameIds.Contains(game.Id)).ToArray();
                _activeGameIds.Clear();
                _activeGameIds.UnionWith(running.Keys);
            }

            foreach (var game in launched)
            {
                lock (_lifecycleGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    GameLaunched?.Invoke(this, game);
                }
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
        Process[] processes;
        try
        {
            processes = _getProcesses();
        }
        catch (InvalidOperationException)
        {
            return found.Values;
        }
        catch (Win32Exception)
        {
            return found.Values;
        }
        catch (UnauthorizedAccessException)
        {
            return found.Values;
        }

        foreach (var process in processes)
        {
            try
            {
                GameDefinition? game = null;
                if (_gamesByPath.Count > 0)
                {
                    try
                    {
                        var runningPath = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(runningPath))
                        {
                            game = _gamesByPath
                                .FirstOrDefault(item => NameNormalizer.ExecutablePathMatches(item.Path, runningPath))
                                .Game;
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
                    {
                        // Fall back to an unambiguous filename mapping below.
                    }
                }

                var processName = NameNormalizer.NormalizeProcessName(process.ProcessName);
                if (game is null && _gamesByProcessName.TryGetValue(processName, out var nameMatch))
                {
                    game = nameMatch;
                }

                if (game is not null)
                {
                    found.TryAdd(game.Id, game);
                }
            }
            catch (InvalidOperationException)
            {
                // The process exited while it was being inspected.
            }
            catch (Win32Exception)
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

    internal IReadOnlyCollection<string> SnapshotActiveGameIds()
    {
        lock (_lifecycleGate)
        {
            return _activeGameIds.ToArray();
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
            _timer.Dispose();
        }
    }
}
