using System.Windows;
using GameReminders.Core;

namespace GameReminders.App;

public partial class GameEditorWindow : Window
{
    private readonly GameDefinition _original;
    private readonly Func<GameDefinition, string?> _save;

    public GameEditorWindow(GameDefinition game, Func<GameDefinition, string?> save)
    {
        InitializeComponent();
        _original = game;
        _save = save;
        NameText.Text = game.Name;
        AliasesText.Text = string.Join(Environment.NewLine, game.Aliases);
        ProcessesText.Text = string.Join(Environment.NewLine, game.Processes);
    }

    public GameDefinition? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidate = _original with
            {
                Name = NameText.Text.Trim(),
                Aliases = SplitLines(AliasesText.Text),
                Processes = SplitLines(ProcessesText.Text)
            };
            JsonProtocol.WriteCatalog(new GameCatalog { Games = [candidate] });
            var saveError = _save(candidate);
            if (!SaveSucceeded(saveError))
            {
                MessageBox.Show($"The game could not be saved.\n\n{saveError}", "Game Reminders", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = candidate;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            MessageBox.Show(exception.Message, "Invalid game", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal static bool SaveSucceeded(string? error) => error is null;

    private static IReadOnlyList<string> SplitLines(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
