using System.Windows;
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

        var catalog = _store.LoadCatalog();
        _monitor?.Dispose();
        _monitor = new ProcessLaunchMonitor(catalog.Games);
        _monitor.GameLaunched += OnGameLaunched;
        _monitor.Start();
        _mainWindow?.SetStatus($"Monitoring {catalog.Games.Count} configured game(s)");
    }

    private void HandleRescanRequested()
    {
        try
        {
            StartMonitoring();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not reload games.json", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnGameLaunched(object? sender, GameDefinition game)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            ShowReminders(game);
        });
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

    protected override void OnExit(ExitEventArgs e)
    {
        _monitor?.Dispose();
        base.OnExit(e);
    }
}

