using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
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

        var sourceType = game.Source?.Type?.Trim();
        var isSteam = string.Equals(sourceType, "steam", StringComparison.OrdinalIgnoreCase);
        SourceTypeText.Text = isSteam ? "Game type: Steam" : "Game type: Manual";
        SourceHelpText.Text = isSteam
            ? @"Steam executable paths are relative to the library's steamapps\common folder."
            : "Manual executable paths may be full paths. Use Browse to select one or more .exe files.";
        BrowseExecutableButton.Visibility = isSteam ? Visibility.Collapsed : Visibility.Visible;

        var candidates = game.Source?.ExecutableCandidates ?? [];
        CandidatesList.ItemsSource = candidates;
        CandidatesLabel.Visibility = CandidatesList.Visibility = CandidatesHelp.Visibility = candidates.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateValidationState();
    }

    public GameDefinition? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var processes = SplitLines(ProcessesText.Text);
            if (!CanSave(_original.Source, processes))
            {
                UpdateValidationState();
                return;
            }

            var source = _original.Source;
            if (source is not null && processes.Count > 0)
            {
                source = source with { RequiresExecutableReview = false };
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

    private void ProcessesText_Changed(object sender, TextChangedEventArgs e) => UpdateValidationState();

    private void UpdateValidationState()
    {
        if (SaveButton is null || ActionRequiredPanel is null || ProcessesText is null)
        {
            return;
        }

        var canSave = CanSave(_original?.Source, SplitLines(ProcessesText.Text));
        SaveButton.IsEnabled = canSave;
        ActionRequiredPanel.Visibility = canSave ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CandidateSelected(object sender, SelectionChangedEventArgs e)
    {
        var selected = CandidatesList.SelectedItems.OfType<string>().ToArray();
        if (selected.Length > 0)
        {
            SetProcesses(MergeExecutablePaths(ProcessesText.Text, selected));
        }
    }

    private void AddCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string candidate })
        {
            SetProcesses(MergeExecutablePaths(ProcessesText.Text, [candidate]));
            e.Handled = true;
        }
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select game executable",
            Filter = "Applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            SetProcesses(MergeExecutablePaths(ProcessesText.Text, dialog.FileNames));
        }
    }

    private void SetProcesses(IReadOnlyList<string> processes)
    {
        ProcessesText.Text = string.Join(Environment.NewLine, processes);
        ProcessesText.CaretIndex = ProcessesText.Text.Length;
    }

    internal static bool SaveSucceeded(string? error) => error is null;

    internal static bool CanSave(GameSource? source, IReadOnlyList<string> processes) =>
        source?.RequiresExecutableReview != true || processes.Count > 0;

    internal static IReadOnlyList<string> MergeExecutablePaths(string existingText, IEnumerable<string> candidates) =>
        SplitLines(existingText)
            .Concat(candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)).Select(candidate => candidate.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> SplitLines(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
