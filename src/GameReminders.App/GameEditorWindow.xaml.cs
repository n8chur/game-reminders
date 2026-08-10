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
        var candidates = game.Source?.ExecutableCandidates ?? [];
        CandidatesText.Text = string.Join(Environment.NewLine, candidates);
        CandidatesLabel.Visibility = CandidatesText.Visibility = candidates.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public GameDefinition? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var processes = SplitLines(ProcessesText.Text);
            var source = _original.Source;
            if (source is not null && processes.Count > 0)
            {
                source = source with
                {
                    RequiresExecutableReview = false,
                    ExecutableCandidates = []
                };
            }
            var candidate = _original with
            {
                Name = NameText.Text.Trim(),
                Aliases = SplitLines(AliasesText.Text),
                Processes = processes,
                Source = source
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
