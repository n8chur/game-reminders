using System.Diagnostics;
using System.Windows;
using GameReminders.Core;

namespace GameReminders.App;

public partial class MainWindow : Window
{
    private readonly string _root;
    private readonly Action _reload;
    private readonly Action _exit;
    private readonly Action _addGame;
    private readonly Action<GameDefinition> _editGame;
    private readonly Action<GameDefinition> _removeGame;
    private readonly Action _scanSteam;
    private readonly Action<PendingGameDetection> _configureDetection;
    private readonly Action<PendingGameDetection> _ignoreDetection;

    public MainWindow(
        string root,
        Action reload,
        Action exit,
        Action addGame,
        Action<GameDefinition> editGame,
        Action<GameDefinition> removeGame,
        Action scanSteam,
        Action<PendingGameDetection> configureDetection,
        Action<PendingGameDetection> ignoreDetection)
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
        RootPathText.Text = root;
        Closing += (_, args) => { args.Cancel = true; Hide(); };
    }

    public void SetStatus(string status) => StatusText.Text = status;

    public void SetGames(IReadOnlyList<GameDefinition> games) => GamesList.ItemsSource = games;

    public void SetPending(IReadOnlyList<PendingGameDetection> pending) => PendingList.ItemsSource = pending;

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", _root) { UseShellExecute = true });

    private void Reload_Click(object sender, RoutedEventArgs e) => _reload();
    private void AddGame_Click(object sender, RoutedEventArgs e) => _addGame();
    private void EditGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is GameDefinition game) _editGame(game);
    }
    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is GameDefinition game) _removeGame(game);
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
    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();
}
