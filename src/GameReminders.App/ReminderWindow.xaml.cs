using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameReminders.Core;

namespace GameReminders.App;

public partial class ReminderWindow : Window
{
    private readonly ReminderStore _store;
    private readonly Dictionary<Guid, Border> _rows = [];

    public ReminderWindow(GameDefinition game, IReadOnlyList<Reminder> reminders, ReminderStore store)
    {
        InitializeComponent();
        _store = store;
        Title = $"{game.Name} reminder";
        HeadingText.Text = reminders.Count == 1
            ? $"{game.Name} reminder"
            : $"{game.Name} reminders";

        foreach (var reminder in reminders)
        {
            AddReminder(reminder);
        }
    }

    private void AddReminder(Reminder reminder)
    {
        var message = new TextBlock
        {
            Text = reminder.Message,
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var dismiss = new Button { Content = "Dismiss", Tag = reminder };
        dismiss.Click += Dismiss_Click;
        var nextLaunch = new Button { Content = "Show on next launch", Tag = reminder, MinWidth = 150 };
        nextLaunch.Click += NextLaunch_Click;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(nextLaunch);
        buttons.Children.Add(dismiss);

        var content = new StackPanel();
        content.Children.Add(message);
        content.Children.Add(buttons);

        var row = new Border
        {
            Child = content,
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _rows[reminder.Id] = row;
        ReminderList.Children.Add(row);
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Reminder reminder })
        {
            return;
        }

        try
        {
            _store.Complete(reminder);
            RemoveReminder(reminder);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The reminder is still pending because it could not be archived.\n\n{exception.Message}",
                "Could not dismiss reminder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void NextLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Reminder reminder })
        {
            RemoveReminder(reminder);
        }
    }

    private void RemoveReminder(Reminder reminder)
    {
        if (_rows.Remove(reminder.Id, out var row))
        {
            ReminderList.Children.Remove(row);
        }

        if (_rows.Count == 0)
        {
            Close();
        }
    }
}
