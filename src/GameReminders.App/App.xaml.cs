using System.Text.Json;
using System.Diagnostics;
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
    private StoreRootValidator? _storeRootValidator;
    private ILaunchAtLoginService? _launchAtLoginService;
    private SingleInstanceCoordinator? _singleInstance;
    private AppSettings _settings = new();
    private ThemeManager? _themeManager;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _normalTrayIcon;
    private System.Drawing.Icon? _attentionTrayIcon;
    private IReadOnlySet<string> _actionRequiredGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly ReviewNotificationQueue _reviewNotifications = new();
    private readonly Dictionary<string, ReminderWindow> _openReminderWindows =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ReminderSessionState _reminderSession = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstance = SingleInstanceCoordinator.TryStart(
                ShouldShowMainWindow(e.Args),
                RequestExistingInstanceWindow);
            if (_singleInstance is null)
            {
                Shutdown();
                return;
            }

            _themeManager = new ThemeManager(this);
            _settingsService = new SettingsService();
            _settings = _settingsService.Load();
            _storeRootValidator = new StoreRootValidator();
            _launchAtLoginService = new LaunchAtLoginService(
                Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable."));
            var root = ResolveRoot();
            if (root is null)
            {
                Shutdown();
                return;
            }

            _store = new ReminderStore(root);
            _store.InvalidReminderDetected += OnInvalidReminderDetected;
            _store.EnsureInitialized();

            var launchAtLogin = GetLaunchAtLoginState(out var startupStatusError);
            _mainWindow = new MainWindow(
                AddManualGame,
                EditGame,
                RemoveGame,
                ScanSteam,
                ConfigureDetection,
                IgnoreDetection,
                RestoreIgnored,
                MarkGamesReviewed,
                launchAtLogin,
                GameReminders.App.MainWindow.CanChangeLaunchAtLogin(startupStatusError),
                SetLaunchAtLogin,
                RefreshReminders,
                ShowNewReminder,
                CompleteReminder,
                DeleteReminder,
                UncompleteReminder,
                ClearCompletedReminders,
                OpenICloudFolder);
            MainWindow = _mainWindow;
            if (startupStatusError is not null)
            {
                _mainWindow.SetStatus(startupStatusError, isIssue: true);
            }
            CreateTrayIcon();
            if (ShouldShowMainWindow(e.Args))
            {
                _mainWindow.Show();
            }

            StartMonitoring();
            RefreshReminders();
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

    internal static bool ShouldShowMainWindow(IReadOnlyList<string> arguments) =>
        !arguments.Any(argument => string.Equals(
            argument,
            LaunchAtLoginService.HiddenAtLoginArgument,
            StringComparison.OrdinalIgnoreCase));

    private string? ResolveRoot()
    {
        if (_storeRootValidator is null || _launchAtLoginService is null || _settingsService is null)
        {
            return null;
        }

        var state = SetupStateResolver.Resolve(_settings, _storeRootValidator.ValidateSavedRoot);
        if (state.Requirement == SetupRequirement.None)
        {
            _settings = ApplyValidatedRoot(_settings, state);
            return _settings.ICloudRoot;
        }

        var launchAtLogin = GetLaunchAtLoginState(out var startupStatusError);
        var suggestedShortcutsRoot =
            ShortcutsFolderLocator.FromSavedRoot(state.SavedRoot) ??
            ShortcutsFolderLocator.Find(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var setup = new SetupWindow(
            state,
            suggestedShortcutsRoot,
            launchAtLogin,
            startupStatusError,
            (selectedRoot, desiredLaunchAtLogin) =>
            {
                var result = SetupCommitter.Commit(
                    _settings,
                    selectedRoot,
                    desiredLaunchAtLogin,
                    _storeRootValidator.ValidateShortcutsSelection,
                    _launchAtLoginService,
                    _settingsService.TrySave);
                if (result.Succeeded)
                {
                    _settings = result.Settings;
                }

                return result.Error;
            });

        return setup.ShowDialog() == true ? _settings.ICloudRoot : null;
    }

    internal static AppSettings ApplyValidatedRoot(AppSettings settings, SetupState state) =>
        state.Requirement == SetupRequirement.None && state.Root is not null
            ? settings with { ICloudRoot = state.Root }
            : settings;

    private bool GetLaunchAtLoginState(out string? error)
    {
        error = null;
        if (_launchAtLoginService is not null &&
            _launchAtLoginService.TryGetEnabled(out var enabled, out error))
        {
            return enabled;
        }

        error ??= "Windows launch-at-login status is unavailable.";
        return false;
    }

    private LaunchAtLoginChangeResult SetLaunchAtLogin(bool enabled)
    {
        if (_launchAtLoginService is null)
        {
            return new LaunchAtLoginChangeResult(false, "Windows launch-at-login is unavailable.");
        }

        if (!_launchAtLoginService.TrySetEnabled(enabled, out var changeError))
        {
            var actualAfterFailure = GetLaunchAtLoginState(out _);
            return new LaunchAtLoginChangeResult(actualAfterFailure, changeError);
        }

        var actual = GetLaunchAtLoginState(out var statusError);
        if (statusError is not null || actual != enabled)
        {
            return new LaunchAtLoginChangeResult(
                actual,
                statusError ?? "Windows did not retain the requested launch-at-login setting.");
        }

        return new LaunchAtLoginChangeResult(actual, null);
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Game Reminders", null, (_, _) => DispatchFromTray(ShowMainWindow));
        menu.Items.Add("Open iCloud Folder", null, (_, _) => DispatchFromTray(OpenICloudFolder));
        menu.Items.Add("Scan Steam", null, (_, _) => DispatchFromTray(ScanSteam));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => DispatchFromTray(ShutdownApplication));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Game Reminders",
            Icon = _normalTrayIcon ??= AppIcon.Create(attention: false),
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
            ? _attentionTrayIcon ??= AppIcon.Create(attention: true)
            : _normalTrayIcon ??= AppIcon.Create(attention: false);
        _trayIcon.Text = attentionCount > 0
            ? $"Game Reminders — {attentionCount} item(s) need review"
            : "Game Reminders";
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
        if (_mainWindow is null)
        {
            return;
        }

        ShowAndActivate(_mainWindow);
        RefreshReminders();
    }

    private void RequestExistingInstanceWindow()
    {
        if (!TrayDispatcher.ShouldDispatch(Dispatcher.HasShutdownStarted, Dispatcher.HasShutdownFinished))
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_mainWindow is not null)
                {
                    ShowMainWindow();
                    return;
                }

                var visibleWindow = Windows.OfType<Window>().FirstOrDefault(window => window.IsVisible);
                if (visibleWindow is not null)
                {
                    ShowAndActivate(visibleWindow);
                }
            });
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            // Ignore an activation request delivered while WPF is shutting down.
        }
    }

    private static void ShowAndActivate(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private void OpenICloudFolder()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ICloudRoot))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _settings.ICloudRoot) { UseShellExecute = true });
        }
    }

    private void ShowGames()
    {
        ShowMainWindow();
        _mainWindow?.ShowGames();
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
            var previous = _monitor;
            var replacement = CreateReplacementMonitor(catalog.Games, previous);
            replacement.GameLaunched += OnGameLaunched;
            _monitor = ActivateReplacementMonitor(replacement, previous);
            _actionRequiredGameIds = catalog.Games
                .Where(game => game.Source?.RequiresExecutableReview == true)
                .Select(game => game.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _mainWindow?.SetGames(catalog.Games, _settings.UnreviewedGameIds.ToHashSet(StringComparer.OrdinalIgnoreCase));
            RefreshIgnored();
            UpdateTrayAttention();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            var status = $"Could not load games.json: {exception.Message}";
            _mainWindow?.SetStatus(status, isIssue: true);
            MessageBox.Show(status, "Could not reload games.json", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static ProcessLaunchMonitor CreateReplacementMonitor(
        IReadOnlyList<GameDefinition> games,
        ProcessLaunchMonitor? previous,
        Func<Process[]>? getProcesses = null) =>
        new(
            games,
            getProcesses ?? Process.GetProcesses,
            previous?.SnapshotActiveGameIds());

    internal static ProcessLaunchMonitor ActivateReplacementMonitor(
        ProcessLaunchMonitor replacement,
        ProcessLaunchMonitor? previous,
        Action<ProcessLaunchMonitor>? start = null)
    {
        previous?.Dispose();
        if (start is null)
        {
            replacement.Start();
        }
        else
        {
            start(replacement);
        }

        return replacement;
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
                $"Remove '{game.Name}' from the catalog? Existing reminder files will be preserved. Steam games can be restored later from Ignored.",
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
        IReadOnlyList<PendingGameDetection> discovered;
        try
        {
            discovered = await Task.Run(() => new SteamGameDiscovery().Discover());
        }
        catch (Exception exception)
        {
            _mainWindow?.SetStatus($"Steam discovery failed: {exception.Message}", isIssue: true);
            return;
        }

        try
        {
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
            var updatedCount = import.UpdatedGames.Count;
            if (added > 0 || updatedCount > 0)
            {
                _store.SaveCatalog(import.Catalog);
                if (added > 0)
                {
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
                        _mainWindow?.SetStatus("Steam games were added, but their review badges could not be saved.", isIssue: true);
                    }
                }
                StartMonitoring();
                if (added > 0)
                {
                    ShowReviewNotification(added, trustedSteamGames: true);
                }
            }
            RemoveConfiguredPending(import.Catalog);
            if (showCompletion)
            {
                _mainWindow?.SetStatus(added > 0
                    ? $"Steam scan added {added} new game(s)"
                    : updatedCount > 0
                        ? $"Steam scan updated {updatedCount} existing game(s)"
                        : "Steam scan found no new games");
            }
        }
        catch (Exception exception)
        {
            _mainWindow?.SetStatus($"Steam scan could not update games.json: {exception.Message}", isIssue: true);
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
            if (IsConfiguredDetection(catalog, detection))
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
            _mainWindow?.SetStatus($"Could not persist detected game '{detection.Name}'; detection will be retried.", isIssue: true);
            return false;
        }
        _settings = updated;
        RefreshPending();
        return true;
    }

    internal static bool IsConfiguredDetection(GameCatalog catalog, PendingGameDetection detection) =>
        catalog.Games.Any(game =>
            (!string.IsNullOrWhiteSpace(detection.AppId) &&
                string.Equals(game.Source?.AppId, detection.AppId, StringComparison.OrdinalIgnoreCase)) ||
            game.Processes.Any(configured =>
                detection.Processes.Any(observed =>
                    NameNormalizer.ExecutableMatches(configured, observed))));

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
                ? "A potential game needs review. Click to open Games."
                : $"{notification.Count} potential games need review. Click to open Games.";
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
            ShowGames();
        }
    }

    private void OnReviewNotificationClosed(object? sender, EventArgs e) =>
        DispatchAfterReviewNotificationClosed(
            _reviewNotifications,
            DispatchFromTray,
            DisplayActiveReviewNotification);

    internal static void DispatchAfterReviewNotificationClosed(
        ReviewNotificationQueue notifications,
        Action<Action> dispatch,
        Action display)
    {
        if (notifications.CompleteActive() is not null)
        {
            dispatch(display);
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
            _mainWindow?.SetStatus("Steam games were added, but cached detections could not be cleaned up.", isIssue: true);
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
            IgnoredDetectionKeys = _settings.IgnoredDetectionKeys.Append(detection.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            IgnoredDetections = _settings.IgnoredDetections.Append(detection)
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray()
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
        RefreshIgnored();
        UpdateTrayAttention();
    }

    private void RefreshIgnored()
    {
        var ignoredDetections = _settings.IgnoredDetections.Select(item => new IgnoredDiscoveryItem(
            item.Key,
            item.Name,
            string.Equals(item.SourceType, "steam", StringComparison.OrdinalIgnoreCase) ? "Steam discovery" : "Detected application"));
        var suppressedSteam = _settings.SuppressedSteamGames.Select(item => new IgnoredDiscoveryItem(
            $"steam:{item.AppId}", item.Name, "Removed Steam game", item.AppId));
        _mainWindow?.SetIgnored(ignoredDetections.Concat(suppressedSteam)
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    private void RestoreIgnored(IgnoredDiscoveryItem item)
    {
        if (item.SteamAppId is null)
        {
            var updatedDetectionSettings = RestoreIgnoredDetection(_settings, item.Key);
            if (_settingsService?.TrySave(updatedDetectionSettings) != true)
            {
                MessageBox.Show("The restore choice could not be saved.", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _settings = updatedDetectionSettings;
            RefreshPending();
            return;
        }

        var updated = _settings with
        {
            SuppressedSteamGames = _settings.SuppressedSteamGames
                .Where(game => !string.Equals(game.AppId, item.SteamAppId, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
        if (_settingsService?.TrySave(updated) != true)
        {
            MessageBox.Show("The restore choice could not be saved.", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings = updated;
        RefreshIgnored();
        UpdateTrayAttention();
        _ = ScanSteamAsync(showCompletion: true);
    }

    internal static AppSettings RestoreIgnoredDetection(AppSettings settings, string key)
    {
        var restored = settings.IgnoredDetections.FirstOrDefault(
            detection => string.Equals(detection.Key, key, StringComparison.OrdinalIgnoreCase));
        var pending = restored is null
            ? settings.PendingDetections
            : settings.PendingDetections
                .Append(restored)
                .GroupBy(detection => detection.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        return settings with
        {
            PendingDetections = pending,
            IgnoredDetections = settings.IgnoredDetections
                .Where(detection => !string.Equals(detection.Key, key, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            IgnoredDetectionKeys = settings.IgnoredDetectionKeys
                .Where(ignoredKey => !string.Equals(ignoredKey, key, StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };
    }

    private void MarkGamesReviewed(IReadOnlyCollection<string> gameIds)
    {
        if (_settings.UnreviewedGameIds.Count == 0 || gameIds.Count == 0) return;
        var reviewedIds = gameIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = _settings.UnreviewedGameIds
            .Where(id => !reviewedIds.Contains(id))
            .ToArray();
        if (remaining.Length == _settings.UnreviewedGameIds.Count) return;
        var updated = _settings with { UnreviewedGameIds = remaining };
        if (_settingsService?.TrySave(updated) != true) return;
        _settings = updated;
        if (_store is not null)
        {
            try
            {
                var catalog = _store.LoadCatalog();
                _mainWindow?.SetGames(catalog.Games, remaining.ToHashSet(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                _mainWindow?.SetStatus($"Could not refresh review badges: {exception.Message}", isIssue: true);
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
                    _mainWindow?.SetStatus(status, isIssue: true);
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
        _reminderSession.BeginNextLaunch(reminders);
        RefreshReminders();
        var window = new ReminderWindow(
            game,
            reminders,
            _store,
            reminder =>
            {
                _reminderSession.Complete(reminder);
                RefreshReminders();
            },
            reminder =>
            {
                _reminderSession.Defer(reminder);
                RefreshReminders();
            });
        _openReminderWindows[game.Id] = window;
        window.Closed += (_, _) => _openReminderWindows.Remove(game.Id);
        window.Show();
    }

    private void RefreshReminders()
    {
        if (_store is null || _mainWindow is null)
        {
            return;
        }

        try
        {
            var catalog = _store.LoadCatalog();
            var names = catalog.Games.ToDictionary(game => game.Id, game => game.Name, StringComparer.OrdinalIgnoreCase);
            var pending = _store.LoadAllPending();
            var completed = _store.LoadCompleted();
            var lists = _reminderSession.Partition(pending, completed, names);
            _mainWindow.SetReminders(lists.Pending, lists.Completed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _mainWindow.SetStatus(
                $"Could not refresh reminders. The last loaded list is still shown. {exception.Message}",
                isIssue: true);
        }
    }

    internal static ReminderListItem ToListItem(Reminder reminder, IReadOnlyDictionary<string, string> catalogNames) =>
        new(reminder, catalogNames.TryGetValue(reminder.GameId, out var currentName)
            ? currentName
            : reminder.GameNameAtCreation);

    private void ShowNewReminder()
    {
        if (_store is null || _mainWindow is null)
        {
            return;
        }

        try
        {
            var games = _store.LoadCatalog().Games;
            if (games.Count == 0)
            {
                MessageBox.Show("Add a game before creating a reminder.", "Game Reminders",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var reminders = _store.LoadAllPending().Concat(_store.LoadCompleted()).ToArray();
            var window = new NewReminderWindow(games, reminders, CreateReminder) { Owner = _mainWindow };
            window.ShowDialog();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            _mainWindow.SetStatus($"Could not open the new reminder form: {exception.Message}", isIssue: true);
        }
    }

    private string? CreateReminder(GameDefinition game, string message)
    {
        if (_store is null)
        {
            return "The reminder store is unavailable.";
        }

        try
        {
            _store.CreatePending(game, message);
            RefreshReminders();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return exception.Message;
        }
    }

    private void CompleteReminder(Reminder reminder)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Complete(reminder);
            _reminderSession.Complete(reminder);
            RefreshReminders();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _mainWindow?.SetStatus(
                $"The reminder is still pending because it could not be completed. {exception.Message}",
                isIssue: true);
        }
    }

    private void DeleteReminder(Reminder reminder)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Delete(reminder);
            _reminderSession.Complete(reminder);
            RefreshReminders();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _mainWindow?.SetStatus(
                $"The reminder was preserved because it could not be deleted. {exception.Message}",
                isIssue: true);
        }
    }

    private void UncompleteReminder(Reminder reminder)
    {
        if (_store is null)
        {
            return;
        }

        try
        {
            _store.Uncomplete(reminder);
            RefreshReminders();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            _mainWindow?.SetStatus(
                $"The reminder stayed completed because it could not be marked pending. {exception.Message}",
                isIssue: true);
        }
    }

    private void ClearCompletedReminders()
    {
        if (_store is null ||
            MessageBox.Show(
                "Clear all completed reminders? Pending reminders will not be affected.",
                "Clear completed reminders",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            foreach (var reminder in _store.LoadCompleted())
            {
                _store.Delete(reminder);
            }

            RefreshReminders();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
        {
            _mainWindow?.SetStatus(
                $"Could not clear all completed reminders. Any remaining reminders were preserved. {exception.Message}",
                isIssue: true);
            RefreshReminders();
        }
    }

    private void OnInvalidReminderDetected(object? sender, InvalidReminderEventArgs e)
    {
        var status = $"Reminder file '{e.FileName}' {e.Reason}.";
        _mainWindow?.SetStatus(status, isIssue: true);
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
        _normalTrayIcon?.Dispose();
        _attentionTrayIcon?.Dispose();
        _themeManager?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
