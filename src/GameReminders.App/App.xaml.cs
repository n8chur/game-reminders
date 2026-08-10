using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using GameReminders.Core;

namespace GameReminders.App;

public partial class App : System.Windows.Application
{
    private ProcessLaunchMonitor? _monitor;
    private ForegroundGameDetector? _foregroundDetector;
    private MainWindow? _mainWindow;
    private ReminderStore? _store;
    private SettingsService? _settingsService;
    private AppSettings _settings = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _attentionTrayIcon;
    private IReadOnlySet<string> _actionRequiredGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly ReviewNotificationQueue _reviewNotifications = new();
    private readonly Dictionary<string, ReminderWindow> _openReminderWindows =
        new(StringComparer.OrdinalIgnoreCase);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _settingsService = new SettingsService();
            _settings = _settingsService.Load();
            var root = ResolveRoot(_settings);
            if (root is null)
            {
                Shutdown();
                return;
            }

            _settings = _settings with { ICloudRoot = root };
            SaveSettings();
            _store = new ReminderStore(root);
            _store.InvalidReminderDetected += OnInvalidReminderDetected;
            _store.EnsureInitialized();

            _mainWindow = new MainWindow(
                root,
                StartMonitoring,
                ShutdownApplication,
                AddManualGame,
                EditGame,
                RemoveGame,
                ScanSteam,
                ConfigureDetection,
                IgnoreDetection,
                RestoreSteamGame,
                MarkGamesReviewed);
            MainWindow = _mainWindow;
            CreateTrayIcon();
            _mainWindow.Show();

            StartMonitoring();
            RefreshPending();
            StartForegroundDetection();
            _ = ScanSteamAsync(showCompletion: false);
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

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Game Reminders", null, (_, _) => DispatchFromTray(ShowMainWindow));
        menu.Items.Add("Scan Steam", null, (_, _) => DispatchFromTray(ScanSteam));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => DispatchFromTray(ShutdownApplication));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Game Reminders",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => DispatchFromTray(ShowMainWindow);
        _trayIcon.BalloonTipClicked += OnReviewNotificationClicked;
        _trayIcon.BalloonTipClosed += OnReviewNotificationClosed;
        UpdateTrayAttention();
    }

    private void UpdateTrayAttention()
    {
        if (_trayIcon is null) return;

        var attentionCount = _settings.UnreviewedGameIds
            .Concat(_actionRequiredGameIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() + _settings.PendingDetections.Count;
        _trayIcon.Icon = attentionCount > 0
            ? _attentionTrayIcon ??= CreateAttentionIcon()
            : System.Drawing.SystemIcons.Application;
        _trayIcon.Text = attentionCount > 0
            ? $"Game Reminders — {attentionCount} item(s) need review"
            : "Game Reminders";
    }

    private static System.Drawing.Icon CreateAttentionIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(16, 16);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.DrawIcon(System.Drawing.SystemIcons.Application, new System.Drawing.Rectangle(0, 0, 16, 16));
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(220, 38, 38));
            graphics.FillEllipse(brush, 9, 0, 7, 7);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void DispatchFromTray(Action action)
    {
        if (!TrayDispatcher.ShouldDispatch(Dispatcher.HasShutdownStarted, Dispatcher.HasShutdownFinished))
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            // Ignore a tray event delivered while WPF is shutting down.
        }
    }

    private void ShowMainWindow()
    {
        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private void ShowGames()
    {
        ShowMainWindow();
        _mainWindow?.ShowGames();
    }

    private void ShowDetectedGames()
    {
        ShowMainWindow();
        _mainWindow?.ShowDetectedGames();
    }

    private void ShutdownApplication()
    {
        _mainWindow?.AllowClose();
        Shutdown();
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
            _actionRequiredGameIds = catalog.Games
                .Where(game => game.Source?.RequiresExecutableReview == true)
                .Select(game => game.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _mainWindow?.SetGames(catalog.Games, _settings.UnreviewedGameIds.ToHashSet(StringComparer.OrdinalIgnoreCase));
            _mainWindow?.SetSuppressedSteamGames(_settings.SuppressedSteamGames);
            UpdateTrayAttention();
            _mainWindow?.SetStatus($"Monitoring {catalog.Games.Count} configured game(s)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            var status = $"Could not load games.json: {exception.Message}";
            _mainWindow?.SetStatus(status);
            MessageBox.Show(status, "Could not reload games.json", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartForegroundDetection()
    {
        _foregroundDetector = new ForegroundGameDetector();
        _foregroundDetector.GameDetected += OnForegroundGameDetected;
        _foregroundDetector.Start();
    }

    private async void OnForegroundGameDetected(object? sender, PendingGameDetection detection)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (AddDetection(detection))
                {
                    ShowReviewNotification(1, trustedSteamGames: false);
                }
            });
        }
        catch (TaskCanceledException)
        {
            // Shutdown canceled the queued UI work.
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            // The dispatcher began shutting down after the initial guard.
        }
    }

    private void AddManualGame() => EditAndSave(new GameDefinition
    {
        Id = $"custom-{Guid.NewGuid():N}",
        Name = "New game",
        Source = new GameSource { Type = "manual" }
    });

    private void EditGame(GameDefinition game) => EditAndSave(game);

    private void EditAndSave(GameDefinition game, string? detectionKey = null)
    {
        var editor = new GameEditorWindow(game, candidate =>
        {
            if (_store is null)
            {
                return "The reminder store is not available.";
            }

            try
            {
                var catalog = _store.LoadCatalog();
                var games = catalog.Games.Where(item => !string.Equals(item.Id, game.Id, StringComparison.OrdinalIgnoreCase))
                    .Append(candidate)
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                _store.SaveCatalog(catalog with { Games = games });
                if (detectionKey is not null)
                {
                    RemovePending(detectionKey);
                }
                StartMonitoring();
                return null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                return exception.Message;
            }
        }) { Owner = _mainWindow };
        editor.ShowDialog();
    }

    private void RemoveGame(GameDefinition game)
    {
        if (_store is null || MessageBox.Show(
                $"Remove '{game.Name}' from the catalog? Existing reminder files will be preserved. Steam games can be restored later from Removed Steam games.",
                "Remove game", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        AppSettings? settingsBeforeRemoval = null;
        try
        {
            var catalog = _store.LoadCatalog();
            var source = game.Source;
            if (source is not null &&
                string.Equals(source.Type, "steam", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(source.AppId))
            {
                var suppressed = new SuppressedSteamGame { AppId = source.AppId, Name = game.Name };
                settingsBeforeRemoval = _settings;
                var updated = _settings with
                {
                    SuppressedSteamGames = _settings.SuppressedSteamGames
                        .Append(suppressed)
                        .GroupBy(item => item.AppId, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .ToArray(),
                    UnreviewedGameIds = _settings.UnreviewedGameIds
                        .Where(id => !string.Equals(id, game.Id, StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                };
                if (_settingsService?.TrySave(updated) != true)
                {
                    MessageBox.Show("The removal choice could not be saved, so the Steam game was not removed.",
                        "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _settings = updated;
            }
            _store.SaveCatalog(catalog with
            {
                Games = catalog.Games.Where(item => !string.Equals(item.Id, game.Id, StringComparison.OrdinalIgnoreCase)).ToArray()
            });
            StartMonitoring();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            if (settingsBeforeRemoval is not null && _settingsService?.TrySave(settingsBeforeRemoval) == true)
            {
                _settings = settingsBeforeRemoval;
                RefreshPending();
            }
            MessageBox.Show($"The game could not be removed.\n\n{exception.Message}", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ScanSteam() => _ = ScanSteamAsync(showCompletion: true);

    private async Task ScanSteamAsync(bool showCompletion)
    {
        try
        {
            var discovered = await Task.Run(() => new SteamGameDiscovery().Discover());
            if (_store is null)
            {
                return;
            }

            var catalog = _store.LoadCatalog();
            var import = SteamCatalogImporter.Import(
                catalog,
                discovered,
                _settings.SuppressedSteamGames.Select(game => game.AppId));
            var added = import.AddedGames.Count;
            if (added > 0)
            {
                _store.SaveCatalog(import.Catalog);
                var updated = _settings with
                {
                    UnreviewedGameIds = _settings.UnreviewedGameIds
                        .Concat(import.AddedGames.Select(game => game.Id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };
                _settings = updated;
                if (_settingsService?.TrySave(updated) != true)
                {
                    _mainWindow?.SetStatus("Steam games were added, but their review badges could not be saved");
                }
                StartMonitoring();
                ShowReviewNotification(added, trustedSteamGames: true);
            }
            RemoveConfiguredPending(import.Catalog);
            if (showCompletion)
            {
                _mainWindow?.SetStatus(added == 0
                    ? "Steam scan found no new games"
                    : $"Steam scan added {added} new game(s)");
            }
        }
        catch (Exception exception)
        {
            _mainWindow?.SetStatus($"Steam scan failed: {exception.Message}");
        }
    }

    private bool AddDetection(PendingGameDetection detection)
    {
        if (_store is null || _settings.IgnoredDetectionKeys.Contains(detection.Key, StringComparer.OrdinalIgnoreCase) ||
            _settings.PendingDetections.Any(item => string.Equals(item.Key, detection.Key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            var catalog = _store.LoadCatalog();
            var detectionProcesses = detection.Processes
                .Select(NameNormalizer.NormalizeExecutableIdentity)
                .Where(process => !string.IsNullOrWhiteSpace(process))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (catalog.Games.Any(game =>
                    (detection.AppId is not null && string.Equals(game.Source?.AppId, detection.AppId, StringComparison.OrdinalIgnoreCase)) ||
                    game.Processes.Any(process => detectionProcesses.Contains(NameNormalizer.NormalizeExecutableIdentity(process)))))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return false;
        }

        var updated = _settings with { PendingDetections = _settings.PendingDetections.Append(detection).ToArray() };
        if (_settingsService?.TrySave(updated) != true)
        {
            _mainWindow?.SetStatus($"Could not persist detected game '{detection.Name}'; detection will be retried");
            return false;
        }
        _settings = updated;
        RefreshPending();
        return true;
    }

    private void ShowReviewNotification(int count, bool trustedSteamGames)
    {
        if (_trayIcon is null || !_reviewNotifications.Enqueue(count, trustedSteamGames))
        {
            return;
        }

        DisplayActiveReviewNotification();
    }

    private void DisplayActiveReviewNotification()
    {
        if (_trayIcon is null || _reviewNotifications.Active is not { } notification)
        {
            return;
        }

        _trayIcon.BalloonTipTitle = "Game Reminders";
        _trayIcon.BalloonTipText = notification.TrustedSteamGames
            ? notification.Count == 1
                ? "A Steam game was added. Click to review it."
                : $"{notification.Count} Steam games were added. Click to review them."
            : notification.Count == 1
                ? "A potential game needs review. Click to open Detected games."
                : $"{notification.Count} potential games need review. Click to open Detected games.";
        _trayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void OnReviewNotificationClicked(object? sender, EventArgs e)
    {
        var notification = _reviewNotifications.Active;
        if (notification?.TrustedSteamGames == true)
        {
            ShowGames();
        }
        else if (notification is not null)
        {
            ShowDetectedGames();
        }
    }

    private void OnReviewNotificationClosed(object? sender, EventArgs e)
    {
        if (_reviewNotifications.CompleteActive() is not null)
        {
            Dispatcher.BeginInvoke(DisplayActiveReviewNotification);
        }
    }

    private void RemoveConfiguredPending(GameCatalog catalog)
    {
        var appIds = catalog.Games
            .Select(game => game.Source?.AppId)
            .Where(appId => !string.IsNullOrWhiteSpace(appId))
            .Select(appId => appId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = _settings.PendingDetections
            .Where(item => string.IsNullOrWhiteSpace(item.AppId) || !appIds.Contains(item.AppId))
            .ToArray();
        if (retained.Length == _settings.PendingDetections.Count)
        {
            return;
        }

        var updated = _settings with { PendingDetections = retained };
        _settings = updated;
        RefreshPending();
        if (_settingsService?.TrySave(updated) != true)
        {
            _mainWindow?.SetStatus("Steam games were added, but cached detections could not be cleaned up");
        }
    }

    private void ConfigureDetection(PendingGameDetection detection)
    {
        var id = detection.SourceType == "steam" && !string.IsNullOrWhiteSpace(detection.AppId)
            ? $"steam-{detection.AppId}"
            : $"custom-{Guid.NewGuid():N}";
        EditAndSave(new GameDefinition
        {
            Id = id,
            Name = detection.Name,
            Processes = detection.Processes,
            Source = new GameSource
            {
                Type = detection.SourceType,
                AppId = detection.AppId,
                RequiresExecutableReview = detection.RequiresExecutableReview,
                ExecutableCandidates = detection.CandidateProcesses
            }
        }, detection.Key);
    }

    private void IgnoreDetection(PendingGameDetection detection)
    {
        var updated = _settings with
        {
            PendingDetections = _settings.PendingDetections.Where(item => !string.Equals(item.Key, detection.Key, StringComparison.OrdinalIgnoreCase)).ToArray(),
            IgnoredDetectionKeys = _settings.IgnoredDetectionKeys.Append(detection.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
        if (_settingsService?.TrySave(updated) != true)
        {
            MessageBox.Show("The ignore choice could not be saved. The game remains pending.", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings = updated;
        RefreshPending();
    }

    private void RemovePending(string key)
    {
        var updated = _settings with
        {
            PendingDetections = _settings.PendingDetections.Where(item => !string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
        if (_settingsService?.TrySave(updated) == true)
        {
            _settings = updated;
        }
        RefreshPending();
    }

    private void RefreshPending()
    {
        _mainWindow?.SetPending(
            _settings.PendingDetections.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        _mainWindow?.SetSuppressedSteamGames(_settings.SuppressedSteamGames);
        UpdateTrayAttention();
    }

    private void RestoreSteamGame(SuppressedSteamGame game)
    {
        var updated = _settings with
        {
            SuppressedSteamGames = _settings.SuppressedSteamGames
                .Where(item => !string.Equals(item.AppId, game.AppId, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
        if (_settingsService?.TrySave(updated) != true)
        {
            MessageBox.Show("The restore choice could not be saved.", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings = updated;
        _mainWindow?.SetSuppressedSteamGames(_settings.SuppressedSteamGames);
        UpdateTrayAttention();
        _ = ScanSteamAsync(showCompletion: true);
    }

    private void MarkGamesReviewed()
    {
        if (_settings.UnreviewedGameIds.Count == 0) return;
        var updated = _settings with { UnreviewedGameIds = [] };
        if (_settingsService?.TrySave(updated) != true) return;
        _settings = updated;
        if (_store is not null)
        {
            try
            {
                var catalog = _store.LoadCatalog();
                _mainWindow?.SetGames(catalog.Games, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                _mainWindow?.SetStatus($"Could not refresh review badges: {exception.Message}");
            }
        }
        UpdateTrayAttention();
    }

    private void SaveSettings() => _settingsService?.Save(_settings);

    private async void OnGameLaunched(object? sender, GameDefinition game)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => ShowReminders(game));
        }
        catch (Exception exception)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    var status = $"Could not display reminders for '{game.Name}': {exception.Message}";
                    _mainWindow?.SetStatus(status);
                    MessageBox.Show(status, "Could not display reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
        }
    }

    private void ShowReminders(GameDefinition game)
    {
        if (_store is null || _openReminderWindows.ContainsKey(game.Id)) return;
        var reminders = _store.LoadPending(game.Id);
        if (reminders.Count == 0) return;
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
        if (_foregroundDetector is not null)
        {
            _foregroundDetector.GameDetected -= OnForegroundGameDetected;
            _foregroundDetector.Dispose();
        }
        _monitor?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _attentionTrayIcon?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
