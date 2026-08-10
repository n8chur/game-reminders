using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameReminders.App;

public sealed class ForegroundGameDetector : IDisposable
{
    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationframehost", "chrome", "code", "discord", "dwm", "explorer", "firefox",
        "gamebar", "msedge", "searchhost", "steam", "textinputhost"
    };
    private readonly System.Threading.Timer _timer;
    private string? _candidate;
    private int _candidateScans;
    private bool _disposed;

    public ForegroundGameDetector() =>
        _timer = new System.Threading.Timer(Scan, null, Timeout.Infinite, Timeout.Infinite);

    public event EventHandler<PendingGameDetection>? GameDetected;

    public void Start() => _timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

    private void Scan(object? state)
    {
        if (_disposed)
        {
            return;
        }

        var detection = TryDetect();
        if (detection is null)
        {
            _candidate = null;
            _candidateScans = 0;
            return;
        }

        if (!string.Equals(_candidate, detection.Key, StringComparison.OrdinalIgnoreCase))
        {
            _candidate = detection.Key;
            _candidateScans = 1;
            return;
        }

        if (++_candidateScans == 3)
        {
            GameDetected?.Invoke(this, detection);
        }
    }

    private static PendingGameDetection? TryDetect()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !IsFullscreen(window))
        {
            return null;
        }

        GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (IgnoredProcesses.Contains(process.ProcessName) || string.IsNullOrWhiteSpace(process.MainWindowTitle))
            {
                return null;
            }

            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new PendingGameDetection
            {
                Key = $"process:{process.ProcessName.ToLowerInvariant()}",
                Name = process.MainWindowTitle,
                Processes = [$"{process.ProcessName}.exe"],
                SourceType = "detected"
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsFullscreen(IntPtr window)
    {
        if (!GetWindowRect(window, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info) &&
            windowRect.Left <= info.Monitor.Left && windowRect.Top <= info.Monitor.Top &&
            windowRect.Right >= info.Monitor.Right && windowRect.Bottom >= info.Monitor.Bottom;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
