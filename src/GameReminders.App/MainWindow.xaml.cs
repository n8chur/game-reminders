using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GameReminders.Core;

namespace GameReminders.App;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private readonly Action _addGame;
    private readonly Action<GameDefinition> _editGame;
    private readonly Action<GameDefinition> _removeGame;
    private readonly Action _scanSteam;
    private readonly Action<PendingGameDetection> _configureDetection;
    private readonly Action<PendingGameDetection> _ignoreDetection;
    private readonly Action<IgnoredDiscoveryItem> _restoreIgnored;
    private readonly Action<IReadOnlyCollection<string>> _markGamesReviewed;
    private readonly Func<bool, LaunchAtLoginChangeResult> _setLaunchAtLogin;
    private readonly Action _refreshReminders;
    private readonly Action _newReminder;
    private readonly Action<Reminder> _completeReminder;
    private readonly Action<Reminder> _deleteReminder;
    private readonly Action<Reminder> _uncompleteReminder;
    private readonly Action _clearCompletedReminders;
    private readonly Action _openICloudFolder;
    private bool _updatingLaunchAtLogin;
    private IReadOnlyList<GameListItem> _games = [];
    private IReadOnlyList<PendingGameDetection> _pending = [];
    private IReadOnlyList<IgnoredDiscoveryItem> _ignored = [];

    internal MainWindow(
        Action addGame,
        Action<GameDefinition> editGame,
        Action<GameDefinition> removeGame,
        Action scanSteam,
        Action<PendingGameDetection> configureDetection,
        Action<PendingGameDetection> ignoreDetection,
        Action<IgnoredDiscoveryItem> restoreIgnored,
        Action<IReadOnlyCollection<string>> markGamesReviewed,
        bool launchAtLogin,
        bool launchAtLoginAvailable,
        Func<bool, LaunchAtLoginChangeResult> setLaunchAtLogin,
        Action refreshReminders,
        Action newReminder,
        Action<Reminder> completeReminder,
        Action<Reminder> deleteReminder,
        Action<Reminder> uncompleteReminder,
        Action clearCompletedReminders,
        Action openICloudFolder)
    {
        InitializeComponent();
        ThemeManager.PrepareWindow(this);
        _addGame = addGame;
        _editGame = editGame;
        _removeGame = removeGame;
        _scanSteam = scanSteam;
        _configureDetection = configureDetection;
        _ignoreDetection = ignoreDetection;
        _restoreIgnored = restoreIgnored;
        _markGamesReviewed = markGamesReviewed;
        _setLaunchAtLogin = setLaunchAtLogin;
        _refreshReminders = refreshReminders;
        _newReminder = newReminder;
        _completeReminder = completeReminder;
        _deleteReminder = deleteReminder;
        _uncompleteReminder = uncompleteReminder;
        _clearCompletedReminders = clearCompletedReminders;
        _openICloudFolder = openICloudFolder;
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        LaunchAtLoginCheckBox.IsEnabled = launchAtLoginAvailable;
        Closing += (_, args) =>
        {
            args.Cancel = ShouldHideOnClose(_allowClose);
            if (args.Cancel)
            {
                MarkVisibleGamesReviewed();
                Hide();
            }
        };
        Deactivated += (_, _) => MarkVisibleGamesReviewed();
        Activated += (_, _) => _refreshReminders();
        ManagementTabs.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, ManagementTabs) && ManagementTabs.SelectedItem == RemindersTab)
            {
                _refreshReminders();
            }
        };
    }

    internal static bool CanChangeLaunchAtLogin(string? statusError) => statusError is null;

    internal static bool ShouldHideOnClose(bool allowClose) => !allowClose;

    public void AllowClose() => _allowClose = true;

    public void SetStatus(string? status, bool isIssue = false)
    {
        StatusText.Text = status ?? string.Empty;
        StatusBanner.Visibility = string.IsNullOrWhiteSpace(status) ? Visibility.Collapsed : Visibility.Visible;
        StatusBanner.Background = (System.Windows.Media.Brush)FindResource(
            isIssue ? "IssueBackgroundBrush" : "NoticeBackgroundBrush");
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            isIssue ? "IssueTextBrush" : "NoticeTextBrush");
    }

    public void SetGames(IReadOnlyList<GameDefinition> games, IReadOnlySet<string> unreviewedGameIds)
    {
        var selectedGameId = (GamesList.SelectedItem as GameListItem)?.Game.Id;
        _games = games.Select(game => new GameListItem(
            game,
            unreviewedGameIds.Contains(game.Id),
            game.Source?.RequiresExecutableReview == true ? "ACTION REQUIRED" :
                unreviewedGameIds.Contains(game.Id) ? "NEW" : string.Empty)).ToArray();
        ApplyGameFilter(selectedGameId);
        var newCount = _games.Count(item => item.IsUnreviewed);
        MyGamesTab.Header = newCount > 0 ? $"My Games ({newCount} new)" : "My Games";
    }

    internal static GameListItem? FindItemByGameId(IEnumerable<GameListItem> items, string? gameId) =>
        string.IsNullOrWhiteSpace(gameId)
            ? null
            : items.FirstOrDefault(item => string.Equals(item.Game.Id, gameId, StringComparison.OrdinalIgnoreCase));

    public void SetPending(IReadOnlyList<PendingGameDetection> pending)
    {
        _pending = pending;
        ApplyGameFilter();
    }

    internal void SetIgnored(IReadOnlyList<IgnoredDiscoveryItem> ignored)
    {
        _ignored = ignored;
        ApplyIgnoredFilter();
    }

    public void ShowGames() => ManagementTabs.SelectedItem = GamesTab;

    internal void SetReminders(
        IReadOnlyList<ReminderListItem> pending,
        IReadOnlyList<ReminderListItem> completed)
    {
        PendingRemindersList.ItemsSource = GroupReminders(pending);
        CompletedRemindersList.ItemsSource = GroupReminders(completed);
        PendingRemindersHeader.Text = $"Pending ({pending.Count})";
        CompletedRemindersHeader.Text = $"Completed ({completed.Count})";
        PendingRemindersEmpty.Visibility = pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedRemindersEmpty.Visibility = completed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PendingRemindersList.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CompletedRemindersList.Visibility = completed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ClearCompletedButton.IsEnabled = completed.Count > 0;
    }

    private static ListCollectionView GroupReminders(IReadOnlyList<ReminderListItem> reminders)
    {
        var view = new ListCollectionView(reminders.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ReminderListItem.GameName)));
        return view;
    }

    private void ApplyGameFilter(string? selectedGameId = null)
    {
        if (GamesList is null || PendingList is null)
        {
            return;
        }

        selectedGameId ??= (GamesList.SelectedItem as GameListItem)?.Game.Id;
        var query = GamesSearchText?.Text?.Trim() ?? string.Empty;
        var games = _games.Where(item => Matches(item.Game.Name, query)).ToArray();
        var pending = _pending.Where(item => Matches(item.Name, query)).ToArray();

        GamesList.ItemsSource = games;
        GamesList.SelectedItem = FindItemByGameId(games, selectedGameId);
        PendingList.ItemsSource = pending;
        DetectedCountText.Text = CountLabel(pending.Length, "item");
        GamesList.Visibility = games.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        GamesEmptyText.Visibility = games.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetectedSection.Visibility = pending.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        GamesSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(GamesSearchText?.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionStates();
    }

    private void ApplyIgnoredFilter()
    {
        if (IgnoredList is null)
        {
            return;
        }

        var query = IgnoredSearchText?.Text?.Trim() ?? string.Empty;
        var ignored = _ignored.Where(item => Matches(item.Name, query)).ToArray();
        IgnoredList.ItemsSource = ignored;
        IgnoredList.Visibility = ignored.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        IgnoredEmptyText.Visibility = ignored.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        IgnoredGamesTab.Header = _ignored.Count > 0 ? $"Ignored ({_ignored.Count})" : "Ignored";
        IgnoredSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(IgnoredSearchText?.Text) ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionStates();
    }

    internal static bool HasSelection(object? selectedItem) => selectedItem is not null;

    private void UpdateSelectionStates()
    {
        EditGameButton.IsEnabled = HasSelection(GamesList.SelectedItem);
        RemoveGameButton.IsEnabled = HasSelection(GamesList.SelectedItem);
        ConfigureDetectionButton.IsEnabled = HasSelection(PendingList.SelectedItem);
        IgnoreDetectionButton.IsEnabled = HasSelection(PendingList.SelectedItem);
        RestoreIgnoredButton.IsEnabled = HasSelection(IgnoredList.SelectedItem);
    }

    internal static bool Matches(string value, string query) =>
        string.IsNullOrWhiteSpace(query) || value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    internal static string CountLabel(int count, string singular) => $"{count} {(count == 1 ? singular : singular + "s")}";

    private void GamesSearchText_Changed(object sender, TextChangedEventArgs e) => ApplyGameFilter();
    private void IgnoredSearchText_Changed(object sender, TextChangedEventArgs e) => ApplyIgnoredFilter();
    private void DismissStatus_Click(object sender, RoutedEventArgs e) => SetStatus(null);
    private void AddGame_Click(object sender, RoutedEventArgs e) => _addGame();
    private void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is GameListItem item) _editGame(item.Game);
    }
    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is GameListItem item) _removeGame(item.Game);
    }
    private void ScanSteam_Click(object sender, RoutedEventArgs e) => _scanSteam();
    private void NewReminder_Click(object sender, RoutedEventArgs e) => _newReminder();
    private void OpenICloudFolder_Click(object sender, RoutedEventArgs e) => _openICloudFolder();
    private void CompleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Reminder reminder })
        {
            _completeReminder(reminder);
        }
    }
    private void CompleteSelectedReminder_Click(object sender, RoutedEventArgs e)
    {
        if (PendingRemindersList.SelectedItem is ReminderListItem item)
        {
            _completeReminder(item.Reminder);
        }
    }
    private void DeleteSelectedReminder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedReminderFromContext(sender) is { } item)
        {
            _deleteReminder(item.Reminder);
        }
    }
    private void UncompleteSelectedReminder_Click(object sender, RoutedEventArgs e)
    {
        if (CompletedRemindersList.SelectedItem is ReminderListItem item)
        {
            _uncompleteReminder(item.Reminder);
        }
    }
    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _clearCompletedReminders();
    private void ReminderList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || sender is not ListBox { SelectedItem: ReminderListItem item })
        {
            return;
        }

        _deleteReminder(item.Reminder);
        e.Handled = true;
    }
    private void ReminderList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
        }
    }
    private void LaunchAtLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_updatingLaunchAtLogin)
        {
            return;
        }

        var result = _setLaunchAtLogin(LaunchAtLoginCheckBox.IsChecked == true);
        _updatingLaunchAtLogin = true;
        LaunchAtLoginCheckBox.IsChecked = result.Enabled;
        _updatingLaunchAtLogin = false;
        if (result.Error is not null)
        {
            SetStatus(result.Error, isIssue: true);
        }
    }
    private void ConfigureDetection_Click(object sender, RoutedEventArgs e)
    {
        if (PendingList.SelectedItem is PendingGameDetection detection) _configureDetection(detection);
    }
    private void IgnoreDetection_Click(object sender, RoutedEventArgs e)
    {
        if (PendingList.SelectedItem is PendingGameDetection detection) _ignoreDetection(detection);
    }
    private void RestoreIgnored_Click(object sender, RoutedEventArgs e)
    {
        if (IgnoredList.SelectedItem is IgnoredDiscoveryItem item) _restoreIgnored(item);
    }

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionStates();
        if (GamesList.SelectedItem is GameListItem item &&
            ShouldAcknowledge(item, isSelected: true, isVisible: false, acknowledgeVisibleRows: false))
        {
            _markGamesReviewed([item.Game.Id]);
        }
    }

    private void PendingList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionStates();

    private void IgnoredList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionStates();

    private void MarkVisibleGamesReviewed()
    {
        if (ManagementTabs.SelectedItem != GamesTab || !GamesList.IsVisible)
        {
            return;
        }

        var visibleIds = GamesList.Items.OfType<GameListItem>()
            .Where(item => ShouldAcknowledge(item, false, IsItemVisible(item), true))
            .Select(item => item.Game.Id)
            .ToArray();
        if (visibleIds.Length > 0) _markGamesReviewed(visibleIds);
    }

    private bool IsItemVisible(GameListItem item)
    {
        if (GamesList.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container ||
            !container.IsVisible)
        {
            return false;
        }

        try
        {
            var origin = container.TranslatePoint(new Point(0, 0), GamesList);
            return IsFullyWithinViewport(new Rect(origin, container.RenderSize), new Rect(new Point(0, 0), GamesList.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool ShouldAcknowledge(GameListItem item, bool isSelected, bool isVisible, bool acknowledgeVisibleRows) =>
        item.IsUnreviewed && (isSelected || (acknowledgeVisibleRows && isVisible));

    internal static bool IsFullyWithinViewport(Rect itemBounds, Rect viewport) =>
        !itemBounds.IsEmpty && viewport.Contains(itemBounds);

    private static ReminderListItem? SelectedReminderFromContext(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: ListBox list } } &&
            list.SelectedItem is ReminderListItem item)
        {
            return item;
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

internal sealed record GameListItem(GameDefinition Game, bool IsUnreviewed, string Badge)
{
    public Visibility BadgeVisibility => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
    public string BadgeBackground => Badge == "ACTION REQUIRED" ? "#B42318" : "#16803A";
    public string SourceLabel => Game.Source?.Type?.Trim().ToLowerInvariant() switch
    {
        "steam" => "Steam",
        "detected" => "Detected application",
        _ => "Manual"
    };
}

internal sealed record IgnoredDiscoveryItem(string Key, string Name, string SourceLabel, string? SteamAppId = null);

internal sealed record LaunchAtLoginChangeResult(bool Enabled, string? Error);

internal sealed record ReminderListItem(Reminder Reminder, string GameName)
{
    public string Details => $"{GameName} · {Reminder.CreatedAt.ToLocalTime():g}";
}
