using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace AnimatedWallPaper.Services;

internal sealed partial class ForegroundAppMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public ForegroundAppMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Refresh();
    }

    public event EventHandler? StateChanged;

    public bool IsFullscreenActive { get; private set; }

    public bool IsOtherAppActive { get; private set; }

    public bool IsOnBattery { get; private set; }

    public int? ForegroundMonitorIndex { get; private set; }

    public int? ExcludedProcessId { get; set; }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void RefreshNow() => Refresh();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _disposed = true;
    }

    private void Refresh()
    {
        var fullscreen = DetectFullscreenApp();
        var otherAppActive = DetectOtherAppActive(out var foregroundMonitorIndex);
        var onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

        if (fullscreen == IsFullscreenActive && otherAppActive == IsOtherAppActive &&
            onBattery == IsOnBattery && foregroundMonitorIndex == ForegroundMonitorIndex)
        {
            return;
        }

        IsFullscreenActive = fullscreen;
        IsOtherAppActive = otherAppActive;
        IsOnBattery = onBattery;
        ForegroundMonitorIndex = foregroundMonitorIndex;
        AppLog.Write($"Foreground state changed. Fullscreen={fullscreen}; OtherAppActive={otherAppActive}; " +
                     $"MonitorIndex={foregroundMonitorIndex?.ToString() ?? "none"}; OnBattery={onBattery}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool DetectFullscreenApp()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground))
        {
            return false;
        }

        if (IsIconic(foreground) || IsWindowCloaked(foreground))
        {
            return false;
        }

        var className = GetClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        if (!GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            cbSize = Marshal.SizeOf<MonitorInfo>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var monitorRect = monitorInfo.rcMonitor;
        const int tolerance = 2;
        return
            windowRect.Left <= monitorRect.Left + tolerance &&
            windowRect.Top <= monitorRect.Top + tolerance &&
            windowRect.Right >= monitorRect.Right - tolerance &&
            windowRect.Bottom >= monitorRect.Bottom - tolerance;
    }

    private bool DetectOtherAppActive(out int? monitorIndex)
    {
        monitorIndex = null;
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || !IsWindowVisible(foreground))
        {
            return false;
        }

        if (IsIconic(foreground) || IsWindowCloaked(foreground))
        {
            return false;
        }

        var className = GetClassName(foreground);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        GetWindowThreadProcessId(foreground, out var processId);
        var isOtherApp = processId != 0 &&
                         processId != (uint)Environment.ProcessId &&
                         processId != (uint?)ExcludedProcessId;
        if (!isOtherApp) return false;

        var screen = Screen.FromHandle(foreground);
        var screens = Screen.AllScreens;
        var index = Array.FindIndex(screens, candidate => candidate.DeviceName == screen.DeviceName);
        monitorIndex = index >= 0 ? index : null;
        return true;
    }

    private static bool IsWindowCloaked(IntPtr window)
    {
        const int cloakedAttribute = 14;
        return DwmGetWindowAttribute(window, cloakedAttribute, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static string GetClassName(IntPtr handle)
    {
        Span<char> buffer = stackalloc char[256];
        var length = GetClassName(handle, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer[..length]);
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(IntPtr hWnd, Span<char> lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int valueSize);
}
