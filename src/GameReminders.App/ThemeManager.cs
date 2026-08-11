using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GameReminders.App;

internal sealed class ThemeManager : IDisposable
{
    private const int DwmwaUseImmersiveDarkMode = 20;
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

    internal static void PrepareWindow(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyNativeTheme(window, IsDarkMode());
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
        Set("PrimaryBrush", "#2563B8");
        Set("PrimaryHoverBrush", dark ? "#3178C6" : "#1E529A");
        Set("PrimaryTextBrush", "#FFFFFF");
        Set("ControlBackgroundBrush", dark ? "#2C3037" : "#FFFFFF");
        Set("ControlHoverBrush", dark ? "#383D46" : "#E8EDF4");
        Set("ControlPressedBrush", dark ? "#454B56" : "#DCE4EE");
        Set("DisabledSurfaceBrush", dark ? "#25282E" : "#F1F3F5");
        Set("DisabledTextBrush", dark ? "#777F8B" : "#9198A3");
        Set("SelectionBrush", dark ? "#273E5A" : "#E5EFFC");
        Set("IssueBackgroundBrush", dark ? "#4A2425" : "#FDECEC");
        Set("IssueTextBrush", dark ? "#FFB4AB" : "#A62A22");
        Set("NoticeBackgroundBrush", dark ? "#203852" : "#E9F2FD");
        Set("NoticeTextBrush", dark ? "#B8D8FF" : "#174F8F");

        foreach (Window window in _application.Windows)
        {
            ApplyNativeTheme(window, dark);
        }
    }

    private static void ApplyNativeTheme(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(
            handle,
            DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
    }

    private void Set(string key, string color) =>
        _application.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private static class NativeMethods
    {
#pragma warning disable SYSLIB1054
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);
#pragma warning restore SYSLIB1054
    }
}
