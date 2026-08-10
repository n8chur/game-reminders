using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using GameReminders.Core;

namespace GameReminders.App;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private readonly string _root;
    private readonly Action _reload;
    private readonly Action _exit;
    private readonly Action _addGame;
    private readonly Action<GameDefinition> _editGame;
    private readonly Action<GameDefinition> _removeGame;
    private readonly Action _scanSteam;
    private readonly Action<PendingGameDetection> _configureDetection;
    private readonly Action<PendingGameDetection> _ignoreDetection;
    private readonly Action<SuppressedSteamGame> _restoreSteamGame;
    private readonly Action<IReadOnlyCollection<string>> _markGamesReviewed;

    public MainWindow(
        string root,
        Action reload,
        Action exit,
        Action addGame,
        Action<GameDefinition> editGame,
        Action<GameDefinition> removeGame,
        Action scanSteam,
        Action<PendingGameDetection> configureDetection,
        Action<PendingGameDetection> ignoreDetection,
        Action<SuppressedSteamGame> restoreSteamGame,
        Action<IReadOnlyCollection<string>> markGamesReviewed)
    {
        InitializeComponent();
        _root = root;
        _reload = reload;
        _exit = exit;
        _addGame = addGame;
        _editGame = editGame;
        _removeGame = removeGame;
        _scanSteam = scanSteam;
        _configureDetection = configureDetection;
        _ignoreDetection = ignoreDetection;
        _restoreSteamGame = restoreSteamGame;
        _markGamesReviewed = markGamesReviewed;
        RootPathText.Text = root;
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
    }

    internal static bool ShouldHideOnClose(bool allowClose) => !allowClose;

    public void AllowClose() => _allowClose = true;

    public void SetStatus(string status) => StatusText.Text = status;

    public void SetGames(IReadOnlyList<GameDefinition> games, IReadOnlySet<string> unreviewedGameIds)
    {
        var selectedGameId = (GamesList.SelectedItem as GameListItem)?.Game.Id;
        var items = games.Select(game => new GameListItem(
            game,
            unreviewedGameIds.Contains(game.Id),
            game.Source?.RequiresExecutableReview == true ? "ACTION REQUIRED" :
                unreviewedGameIds.Contains(game.Id) ? "NEW" : string.Empty)).ToArray();
        GamesList.ItemsSource = items;
        GamesList.SelectedItem = FindItemByGameId(items, selectedGameId);
        var newCount = items.Count(item => item.IsUnreviewed);
        GamesTab.Header = newCount > 0 ? $"Games ({newCount} new)" : "Games";
    }

    internal static GameListItem? FindItemByGameId(IEnumerable<GameListItem> items, string? gameId) =>
        string.IsNullOrWhiteSpace(gameId)
            ? null
            : items.FirstOrDefault(item => string.Equals(item.Game.Id, gameId, StringComparison.OrdinalIgnoreCase));

    public void SetPending(IReadOnlyList<PendingGameDetection> pending) => PendingList.ItemsSource = pending;

    public void SetSuppressedSteamGames(IReadOnlyList<SuppressedSteamGame> games) =>
        SuppressedSteamList.ItemsSource = games;

    public void ShowGames()
    {
        ManagementTabs.SelectedItem = GamesTab;
    }

    public void ShowDetectedGames() => ManagementTabs.SelectedItem = DetectedGamesTab;

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", _root) { UseShellExecute = true });

    private void Reload_Click(object sender, RoutedEventArgs e) => _reload();
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
    private void ConfigureDetection_Click(object sender, RoutedEventArgs e)
    {
        if (PendingList.SelectedItem is PendingGameDetection detection) _configureDetection(detection);
    }
    private void IgnoreDetection_Click(object sender, RoutedEventArgs e)
    {
        if (PendingList.SelectedItem is PendingGameDetection detection) _ignoreDetection(detection);
    }
    private void RestoreSteamGame_Click(object sender, RoutedEventArgs e)
    {
        if (SuppressedSteamList.SelectedItem is SuppressedSteamGame game) _restoreSteamGame(game);
    }
    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        MarkVisibleGamesReviewed();
        Hide();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();

    private void GamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GamesList.SelectedItem is GameListItem item &&
            ShouldAcknowledge(item, isSelected: true, isVisible: false, acknowledgeVisibleRows: false))
        {
            _markGamesReviewed([item.Game.Id]);
        }
    }

    private void MarkVisibleGamesReviewed()
    {
        if (ManagementTabs.SelectedItem != GamesTab || !GamesList.IsVisible)
        {
            return;
        }

        var visibleIds = GamesList.Items.OfType<GameListItem>()
            .Where(item => ShouldAcknowledge(
                item,
                isSelected: false,
                isVisible: IsItemVisible(item),
                acknowledgeVisibleRows: true))
            .Select(item => item.Game.Id)
            .ToArray();
        if (visibleIds.Length > 0)
        {
            _markGamesReviewed(visibleIds);
        }
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
            var itemBounds = new Rect(origin, container.RenderSize);
            var viewport = new Rect(new Point(0, 0), GamesList.RenderSize);
            return IsFullyWithinViewport(itemBounds, viewport);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool ShouldAcknowledge(
        GameListItem item,
        bool isSelected,
        bool isVisible,
        bool acknowledgeVisibleRows) =>
        item.IsUnreviewed && (isSelected || (acknowledgeVisibleRows && isVisible));

    internal static bool IsFullyWithinViewport(Rect itemBounds, Rect viewport) =>
        !itemBounds.IsEmpty && viewport.Contains(itemBounds);
}

internal sealed record GameListItem(GameDefinition Game, bool IsUnreviewed, string Badge)
{
    public Visibility BadgeVisibility => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
}
