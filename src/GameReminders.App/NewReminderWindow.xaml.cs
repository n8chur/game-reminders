using System.Windows;
using System.Windows.Controls;
using GameReminders.Core;

namespace GameReminders.App;

public partial class NewReminderWindow : Window
{
    private readonly Func<GameDefinition, string, string?> _create;

    internal NewReminderWindow(
        IReadOnlyList<GameDefinition> games,
        IReadOnlyList<Reminder> reminders,
        Func<GameDefinition, string, string?> create)
    {
        InitializeComponent();
        ThemeManager.PrepareWindow(this);
        _create = create;
        var sortedGames = games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        GamePicker.ItemsSource = sortedGames;
        var defaultGame = ChooseDefaultGame(sortedGames, reminders);
        if (defaultGame is not null)
        {
            GamePicker.SelectedItem = defaultGame;
        }
        MessageText.Focus();
        UpdateCreateState();
    }

    private void Input_Changed(object sender, RoutedEventArgs e) => UpdateCreateState();

    private void UpdateCreateState()
    {
        if (CreateButton is not null)
        {
            CreateButton.IsEnabled = CanCreate(GamePicker?.SelectedItem as GameDefinition, MessageText?.Text);
        }
    }

    internal static bool CanCreate(GameDefinition? game, string? message) =>
        game is not null && !string.IsNullOrWhiteSpace(message);

    internal static GameDefinition? ChooseDefaultGame(
        IReadOnlyList<GameDefinition> games,
        IReadOnlyList<Reminder> reminders)
    {
        var gamesById = games.ToDictionary(game => game.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var reminder in reminders.OrderByDescending(reminder => reminder.CreatedAt))
        {
            if (gamesById.TryGetValue(reminder.GameId, out var game))
            {
                return game;
            }
        }

        return games.FirstOrDefault();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (GamePicker.SelectedItem is not GameDefinition game || !CanCreate(game, MessageText.Text))
        {
            return;
        }

        var error = _create(game, MessageText.Text);
        if (error is not null)
        {
            MessageBox.Show($"The reminder could not be created.\n\n{error}",
                "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
