using System.Windows;

namespace GameReminders.App;

public partial class SetupWindow : Window
{
    private readonly Func<string?, bool, string?> _completeSetup;

    internal SetupWindow(
        SetupState state,
        bool launchAtLogin,
        string? startupStatusError,
        Func<string?, bool, string?> completeSetup)
    {
        InitializeComponent();
        ThemeManager.PrepareWindow(this);
        _completeSetup = completeSetup;
        LaunchAtLoginCheckBox.IsChecked = launchAtLogin;
        FolderPathText.Text = state.SavedRoot ?? string.Empty;

        if (state.Requirement == SetupRequirement.RecoverFolder)
        {
            Title = "Reconnect Game Reminders";
            HeadingText.Text = "Reconnect your iCloud folder";
            IntroductionText.Text =
                "The saved reminder folder is unavailable or cannot be used. Its saved location will remain unchanged unless you select and confirm a replacement.";
            ErrorText.Text = state.Error ?? string.Empty;
        }
        else
        {
            IntroductionText.Text =
                "Choose the existing folder shared with the Game Reminder Shortcut before monitoring begins.";
            ErrorText.Text = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(startupStatusError))
        {
            ErrorText.Text = string.Join(Environment.NewLine, new[] { ErrorText.Text, startupStatusError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select iCloud Drive/Shortcuts/Game Reminders",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(FolderPathText.Text) ? FolderPathText.Text : string.Empty
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathText.Text = dialog.SelectedPath;
            ErrorText.Text = string.Empty;
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var error = _completeSetup(FolderPathText.Text, LaunchAtLoginCheckBox.IsChecked == true);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        DialogResult = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
