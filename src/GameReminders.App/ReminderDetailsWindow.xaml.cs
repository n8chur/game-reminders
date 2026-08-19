using System.Windows;
using System.Windows.Controls;
using GameReminders.Core;

namespace GameReminders.App;

public partial class ReminderDetailsWindow : Window
{
    private readonly Reminder _original;
    private readonly bool _editable;
    private readonly Func<Reminder, GameDefinition, string, ReminderUpdateResult>? _save;

    internal ReminderDetailsWindow(
        Reminder reminder,
        IReadOnlyList<GameDefinition> games,
        bool editable,
        Func<Reminder, GameDefinition, string, ReminderUpdateResult>? save = null)
    {
        InitializeComponent();
        ThemeManager.PrepareWindow(this);
        _original = reminder;
        _editable = editable;
        _save = save;

        var options = BuildGameOptions(reminder, games);
        GamePicker.ItemsSource = options;
        var selectedGame = options.First(option =>
            string.Equals(option.Game.Id, reminder.GameId, StringComparison.OrdinalIgnoreCase));
        GamePicker.SelectedItem = selectedGame;
        GamePicker.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        GameDisplaySurface.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        GameDisplayText.Text = selectedGame.DisplayName;
        MessageText.Text = reminder.Message;
        MessageText.IsReadOnly = !editable;
        CreatedText.Text = $"Created {reminder.CreatedAt.ToLocalTime():f}";
        Title = editable ? "Edit reminder" : "View reminder";
        HeadingText.Text = Title;
        EditButtons.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        ViewButtons.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        if (editable)
        {
            MessageText.Focus();
            MessageText.CaretIndex = MessageText.Text.Length;
        }
        UpdateSaveState();
    }

    internal Reminder? Result { get; private set; }

    internal static IReadOnlyList<ReminderGameOption> BuildGameOptions(
        Reminder reminder,
        IReadOnlyList<GameDefinition> games)
    {
        var options = games
            .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(game => new ReminderGameOption(game, false))
            .ToList();
        if (!options.Any(option => string.Equals(
                option.Game.Id,
                reminder.GameId,
                StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, new ReminderGameOption(
                new GameDefinition { Id = reminder.GameId, Name = reminder.GameNameAtCreation },
                true));
        }

        return options;
    }

    internal static bool CanSave(bool editable, GameDefinition? game, string? message) =>
        editable && game is not null && !string.IsNullOrWhiteSpace(message);

    private void Input_Changed(object sender, RoutedEventArgs e) => UpdateSaveState();

    private void UpdateSaveState()
    {
        if (SaveButton is not null)
        {
            SaveButton.IsEnabled = CanSave(
                _editable,
                (GamePicker?.SelectedItem as ReminderGameOption)?.Game,
                MessageText?.Text);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if ((GamePicker.SelectedItem as ReminderGameOption)?.Game is not { } game ||
            !CanSave(_editable, game, MessageText.Text) ||
            _save is null)
        {
            return;
        }

        var result = _save(_original, game, MessageText.Text);
        if (result.Error is not null || result.Reminder is null)
        {
            MessageBox.Show(
                $"The reminder could not be saved.\n\n{result.Error ?? "No updated reminder was returned."}",
                "Game Reminders",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = result.Reminder;
        DialogResult = true;
    }
}

internal sealed record ReminderGameOption(GameDefinition Game, bool IsUnavailable)
{
    public string DisplayName => IsUnavailable ? $"{Game.Name} (not configured)" : Game.Name;
}

internal sealed record ReminderUpdateResult(Reminder? Reminder, string? Error);
