using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Threading;

namespace AnimatedWallPaper.Services;

internal sealed partial class ForegroundAppMonitor : IDisposable
{
    // Periodic detection runs on a background timer so the heavy EnumWindows sweep (many P/Invokes
    // per top-level window, once a second) no longer stalls the UI thread. The resulting state is
    // applied back on the owner Dispatcher, so properties and StateChanged stay single-threaded
    // for subscribers. RefreshNow() stays synchronous for callers
    // that read the state immediately after (wallpaper start/recovery).
    private readonly System.Threading.Timer _timer;
    private readonly Dispatcher _dispatcher;
    private int _ticking;
    private volatile bool _disposed;

    public ForegroundAppMonitor()
    {
        // MainWindow can construct this service before Application.Run installs WPF's
        // synchronization context. A default SynchronizationContext posts to the thread
        // pool, so capture the actual dispatcher independently of the ambient context.
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new System.Threading.Timer(_ => BackgroundTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler? StateChanged;

    // Monitor indices (into Screen.AllScreens, matching DesktopWorker.GetMonitorTargets order) whose display
    // is fully covered by a foreign app window (borderless/fullscreen).
    public IReadOnlyList<int> FullscreenMonitors { get; private set; } = Array.Empty<int>();

    // Monitor indices covered by a maximized OR fullscreen foreign app window (superset of FullscreenMonitors).
    public IReadOnlyList<int> CoveredMonitors { get; private set; } = Array.Empty<int>();

    public bool IsOnBattery { get; private set; }

    public int? ExcludedProcessId { get; set; }

    public void Start()
    {
        Refresh();
        _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void RefreshNow() => Refresh();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    // Background timer callback: does the heavy detection off the UI thread, then marshals the
    // state application back to the UI. The _ticking guard drops a tick if the previous one is
    // still running (e.g. a very busy desktop), so ticks never pile up.
    private void BackgroundTick()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished ||
            Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            var (fullscreen, covered) = DetectCoveredMonitors();
            var onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            _dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => Apply(fullscreen, covered, onBattery)));
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Foreground detection failed", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    // Synchronous detection + apply on the calling (UI) thread; used at startup and on demand.
    private void Refresh()
    {
        if (_disposed) return;
        _dispatcher.VerifyAccess();
        var (fullscreen, covered) = DetectCoveredMonitors();
        var onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        Apply(fullscreen, covered, onBattery);
    }

    // Always runs on the UI thread (direct from Refresh, or posted from BackgroundTick), so the
    // state fields and StateChanged are never touched concurrently.
    private void Apply(IReadOnlyList<int> fullscreen, IReadOnlyList<int> covered, bool onBattery)
    {
        if (_disposed) return;
        _dispatcher.VerifyAccess();
        if (onBattery == IsOnBattery && SameSet(fullscreen, FullscreenMonitors) && SameSet(covered, CoveredMonitors))
        {
            return;
        }

        FullscreenMonitors = fullscreen;
        CoveredMonitors = covered;
        IsOnBattery = onBattery;
        AppLog.Write($"Foreground state changed. Fullscreen=[{string.Join(",", fullscreen)}]; " +
                     $"Covered=[{string.Join(",", covered)}]; OnBattery={onBattery}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // Enumerates every visible top-level window and, per monitor, records whether a foreign app window covers
    // it. A window "covers" a monitor when its rectangle spans the whole monitor (fullscreen) or the whole
    // work area (maximized). This is per monitor, so every occupied display freezes independently while a
    // clean display keeps animating.
    private (IReadOnlyList<int> Fullscreen, IReadOnlyList<int> Covered) DetectCoveredMonitors()
    {
        var screens = Screen.AllScreens;
        var fullscreen = new SortedSet<int>();
        var covered = new SortedSet<int>();
        var ownProcessId = (uint)Environment.ProcessId;
        var excluded = (uint?)ExcludedProcessId;

        bool Callback(IntPtr window, IntPtr _)
        {
            if (!IsWindowVisible(window) || IsIconic(window) || IsWindowCloaked(window))
            {
                return true;
            }

            var className = GetClassName(window);
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || processId == ownProcessId || processId == excluded)
            {
                return true;
            }

            // Skip overlays that visually cover a monitor but are not apps (e.g. the NVIDIA GeForce overlay).
            if (!AppWindowFilter.IsEligibleAppWindow(GetWindowLong(window, GwlExStyle)))
            {
                return true;
            }

            if (!GetWindowRect(window, out var windowRect) ||
                windowRect.Right <= windowRect.Left || windowRect.Bottom <= windowRect.Top)
            {
                return true;
            }

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return true;
            }

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return true;
            }

            var screen = Screen.FromHandle(window);
            var index = Array.FindIndex(screens, candidate => candidate.DeviceName == screen.DeviceName);
            if (index < 0)
            {
                return true;
            }

            var coverage = MonitorCoverage.Classify(
                new MonitorCoverage.Rectangle(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom),
                new MonitorCoverage.Rectangle(monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top,
                    monitorInfo.rcMonitor.Right, monitorInfo.rcMonitor.Bottom),
                new MonitorCoverage.Rectangle(monitorInfo.rcWork.Left, monitorInfo.rcWork.Top,
                    monitorInfo.rcWork.Right, monitorInfo.rcWork.Bottom));
            if (coverage == MonitorCoverage.Coverage.Fullscreen)
            {
                fullscreen.Add(index);
                covered.Add(index);
            }
            else if (coverage == MonitorCoverage.Coverage.Maximized)
            {
                covered.Add(index);
            }

            return true;
        }

        EnumWindows(Callback, IntPtr.Zero);
        return (fullscreen.ToArray(), covered.ToArray());
    }

    private static bool SameSet(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

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
    private const int GwlExStyle = -20;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int valueSize);
}
