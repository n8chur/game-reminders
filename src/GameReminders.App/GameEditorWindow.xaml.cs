using System.Windows;
using GameReminders.Core;

namespace GameReminders.App;

public partial class GameEditorWindow : Window
{
    private readonly GameDefinition _original;

    public GameEditorWindow(GameDefinition game)
    {
        InitializeComponent();
        _original = game;
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
            Result = candidate;
            DialogResult = true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            MessageBox.Show(exception.Message, "Invalid game", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static IReadOnlyList<string> SplitLines(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
