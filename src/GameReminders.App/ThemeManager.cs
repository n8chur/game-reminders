using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace GameReminders.App;

internal sealed class ThemeManager : IDisposable
{
    private readonly System.Windows.Application _application;

    public ThemeManager(System.Windows.Application application)
    {
        _application = application;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Apply();
    }

    internal static bool IsDarkMode()
    {
        if (SystemParameters.HighContrast)
        {
            return false;
        }

        using var personalize = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return personalize?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        _application.Dispatcher.BeginInvoke(Apply);

    private void Apply()
    {
        var dark = IsDarkMode();
        Set("WindowBackground", dark ? "#181A1F" : "#F6F7F9");
        Set("SurfaceBrush", dark ? "#22252B" : "#FFFFFF");
        Set("SurfaceMutedBrush", dark ? "#2C3037" : "#EEF1F5");
        Set("TextBrush", dark ? "#F2F4F7" : "#171A1F");
        Set("SecondaryTextBrush", dark ? "#B6BDC8" : "#5F6672");
        Set("BorderBrush", dark ? "#414650" : "#D8DCE3");
        Set("PrimaryBrush", dark ? "#73AAEB" : "#2563B8");
        Set("PrimaryHoverBrush", dark ? "#93BEF0" : "#1E529A");
        Set("SelectionBrush", dark ? "#273E5A" : "#E5EFFC");
        Set("IssueBackgroundBrush", dark ? "#4A2425" : "#FDECEC");
        Set("IssueTextBrush", dark ? "#FFB4AB" : "#A62A22");
        Set("NoticeBackgroundBrush", dark ? "#203852" : "#E9F2FD");
        Set("NoticeTextBrush", dark ? "#B8D8FF" : "#174F8F");
    }

    private void Set(string key, string color) =>
        _application.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
