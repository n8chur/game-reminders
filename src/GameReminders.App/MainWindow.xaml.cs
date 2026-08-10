using System.Diagnostics;
using System.Windows;
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
    private readonly Action _markGamesReviewed;
    private bool _gamesSeenSinceActivation;

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
        Action markGamesReviewed)
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
                MarkGamesReviewedIfSeen();
                Hide();
            }
        };
        Activated += (_, _) => _gamesSeenSinceActivation = ManagementTabs.SelectedItem == GamesTab;
        Deactivated += (_, _) => MarkGamesReviewedIfSeen();
        ManagementTabs.SelectionChanged += (_, _) =>
        {
            if (ManagementTabs.SelectedItem == GamesTab && IsVisible)
            {
                _gamesSeenSinceActivation = true;
            }
        };
    }

    internal static bool ShouldHideOnClose(bool allowClose) => !allowClose;

    public void AllowClose() => _allowClose = true;

    public void SetStatus(string status) => StatusText.Text = status;

    public void SetGames(IReadOnlyList<GameDefinition> games, IReadOnlySet<string> unreviewedGameIds)
    {
        GamesList.ItemsSource = games.Select(game => new GameListItem(
            game,
            game.Source?.RequiresExecutableReview == true ? "ACTION REQUIRED" :
                unreviewedGameIds.Contains(game.Id) ? "NEW" : string.Empty)).ToArray();
        GamesTab.Header = unreviewedGameIds.Count > 0 ? $"Games ({unreviewedGameIds.Count} new)" : "Games";
    }

    public void SetPending(IReadOnlyList<PendingGameDetection> pending) => PendingList.ItemsSource = pending;

    public void SetSuppressedSteamGames(IReadOnlyList<SuppressedSteamGame> games) =>
        SuppressedSteamList.ItemsSource = games;

    public void ShowGames()
    {
        ManagementTabs.SelectedItem = GamesTab;
        if (IsVisible) _gamesSeenSinceActivation = true;
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
        MarkGamesReviewedIfSeen();
        Hide();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();

    private void MarkGamesReviewedIfSeen()
    {
        if (!_gamesSeenSinceActivation) return;
        _gamesSeenSinceActivation = false;
        _markGamesReviewed();
    }
}

internal sealed record GameListItem(GameDefinition Game, string Badge)
{
    public Visibility BadgeVisibility => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
}
