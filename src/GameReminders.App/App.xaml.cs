using System.Windows;
using System.Text.Json;
using GameReminders.Core;

namespace GameReminders.App;

public partial class App : System.Windows.Application
{
    private ProcessLaunchMonitor? _monitor;
    private MainWindow? _mainWindow;
    private ReminderStore? _store;
    private readonly Dictionary<string, ReminderWindow> _openReminderWindows =
        new(StringComparer.OrdinalIgnoreCase);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var root = ResolveRoot(settings);
            if (root is null)
            {
                Shutdown();
                return;
            }

            settingsService.Save(new AppSettings { ICloudRoot = root });
            _store = new ReminderStore(root);
            _store.InvalidReminderDetected += OnInvalidReminderDetected;
            _store.EnsureInitialized();

            _mainWindow = new MainWindow(root, HandleRescanRequested, Shutdown);
            MainWindow = _mainWindow;
            _mainWindow.Show();

            StartMonitoring();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Game Reminders could not start.\n\n{exception.Message}",
                "Game Reminders",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string? ResolveRoot(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ICloudRoot) && Directory.Exists(settings.ICloudRoot))
        {
            return settings.ICloudRoot;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select your iCloud Drive 'Game Reminders' folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private void StartMonitoring()
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            var catalog = _store.LoadCatalog();
            var replacement = new ProcessLaunchMonitor(catalog.Games);
            replacement.GameLaunched += OnGameLaunched;
            replacement.Start();

            var previous = _monitor;
            _monitor = replacement;
            previous?.Dispose();
            _mainWindow?.SetStatus($"Monitoring {catalog.Games.Count} configured game(s)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            var status = $"Could not load games.json: {exception.Message}";
            _mainWindow?.SetStatus(status);
            MessageBox.Show(status, "Could not reload games.json", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void HandleRescanRequested()
    {
        StartMonitoring();
    }

    private async void OnGameLaunched(object? sender, GameDefinition game)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => ShowReminders(game));
        }
        catch (Exception exception)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var status = $"Could not display reminders for '{game.Name}': {exception.Message}";
                    _mainWindow?.SetStatus(status);
                    MessageBox.Show(status, "Could not display reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (TaskCanceledException)
            {
                // The application shut down while the failure was being reported.
            }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                // The dispatcher completed shutdown between the check and invocation.
            }
        }
    }

    private void ShowReminders(GameDefinition game)
    {
        if (_store is null || _openReminderWindows.ContainsKey(game.Id))
        {
            return;
        }

        var reminders = _store.LoadPending(game.Id);
        if (reminders.Count == 0)
        {
            return;
        }

        var window = new ReminderWindow(game, reminders, _store);
        _openReminderWindows[game.Id] = window;
        window.Closed += (_, _) => _openReminderWindows.Remove(game.Id);
        window.Show();
    }

    private void OnInvalidReminderDetected(object? sender, InvalidReminderEventArgs e)
    {
        var status = $"Reminder file '{e.FileName}' {e.Reason}.";
        _mainWindow?.SetStatus(status);
        MessageBox.Show(status, "Invalid reminder file", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        base.OnExit(e);
    }
}
