using System.Diagnostics;
using System.Windows;

namespace GameReminders.App;

public partial class MainWindow : Window
{
    private readonly string _root;
    private readonly Action _reload;
    private readonly Action _exit;

    public MainWindow(string root, Action reload, Action exit)
    {
        InitializeComponent();
        _root = root;
        _reload = reload;
        _exit = exit;
        RootPathText.Text = root;
    }

    public void SetStatus(string status) => StatusText.Text = status;

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", _root) { UseShellExecute = true });
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _reload();

    private void Exit_Click(object sender, RoutedEventArgs e) => _exit();

}
