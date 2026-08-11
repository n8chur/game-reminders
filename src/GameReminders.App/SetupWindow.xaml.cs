using System.Windows;

namespace GameReminders.App;

public partial class SetupWindow : Window
{
    private readonly Func<string?, bool, string?> _completeSetup;

    internal SetupWindow(
        SetupState state,
        string? suggestedShortcutsRoot,
        bool launchAtLogin,
        string? startupStatusError,
        Func<string?, bool, string?> completeSetup)
    {
        InitializeComponent();
        ThemeManager.PrepareWindow(this);
        _completeSetup = completeSetup;
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        SetSelectedFolder(suggestedShortcutsRoot);

        if (state.Requirement == SetupRequirement.RecoverFolder)
        {
            Title = "Reconnect Game Reminders";
            HeadingText.Text = "Reconnect your iCloud folder";
            IntroductionText.Text =
                "The saved Game Reminders folder cannot be used. Select Shortcuts in iCloud Drive to reconnect or recreate the required subfolder.";
            ErrorText.Text = state.Error ?? string.Empty;
        }
        else
        {
            IntroductionText.Text =
                "Select the Shortcuts folder in iCloud Drive. Game Reminders handles its required subfolder.";
            ErrorText.Text = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(startupStatusError))
        {
            LaunchAtLoginCheckBox.IsEnabled = false;
            ErrorText.Text = string.Join(Environment.NewLine, new[] { ErrorText.Text, startupStatusError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the Shortcuts folder inside iCloud Drive",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_selectedShortcutsRoot) ? _selectedShortcutsRoot : string.Empty
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SetSelectedFolder(dialog.SelectedPath);
            ErrorText.Text = string.Empty;
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var error = _completeSetup(_selectedShortcutsRoot, LaunchAtLoginCheckBox.IsChecked == true);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private string? _selectedShortcutsRoot;

    private void SetSelectedFolder(string? path)
    {
        _selectedShortcutsRoot = path;
        FolderPathText.Text = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : ShortcutsFolderLocator.ToDisplayPath(path);
        FolderPathText.ToolTip = "Selected iCloud Drive Shortcuts folder";
    }
}
