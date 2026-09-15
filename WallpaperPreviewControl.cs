using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public sealed class WallpaperPreviewControl : HwndHost
{
    private readonly WallpaperController _controller = new();
    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private IntPtr _container;
    private WallpaperRequest? _request;
    private bool _suspended;
    private bool _disposed;
    public event Action<string>? StatusChanged;

    public WallpaperPreviewControl()
    {
        _resizeTimer.Tick += async (_, _) => { _resizeTimer.Stop(); await RefreshAsync(); };
        IsVisibleChanged += (_, _) => Schedule();
        SizeChanged += (_, _) => Schedule();
    }

    internal void Select(WallpaperRequest? request)
    {
        _request = request;
        _controller.Stop();
        Schedule();
    }

    internal void SetSuspended(bool suspended)
    {
        if (_suspended == suspended) return;
        _suspended = suspended;
        Schedule();
    }

    internal void UpdateSettings(VisualizerSettings settings)
    {
        if (_request is not null) _request = _request with { Settings = settings };
        _controller.UpdateVisualizerSettings(settings);
    }
    internal void SetFrameCap(int fps)
    {
        if (_request is not null) _request = _request with { FramesPerSecond = fps };
        _controller.SetFrameCap(fps);
    }

    internal void SetAudioEnabled(bool enabled) => _controller.SetAudioEnabled(enabled);

    private void Schedule()
    {
        _resizeTimer.Stop();
        if (_disposed || _suspended || !IsVisible) { _controller.Stop(); return; }
        _resizeTimer.Start();
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _suspended || !IsVisible || _container == IntPtr.Zero || _request is null) return;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var target = new PreviewTarget(_container, Math.Max(2, (int)(ActualWidth * dpi.DpiScaleX)),
            Math.Max(2, (int)(ActualHeight * dpi.DpiScaleY)));
        try
        {
            StatusChanged?.Invoke("Preparing preview…");
            await _controller.StartAsync(_request with { Preview = target });
            StatusChanged?.Invoke("Live preview");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AppLog.WriteException("Preview unavailable", exception);
            StatusChanged?.Invoke("Preview unavailable: " + exception.Message);
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _container = CreateWindowEx(0, "STATIC", "HYPNIX preview", 0x40000000 | 0x10000000 | 0x02000000,
            0, 0, 2, 2, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_container == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Schedule();
        return new HandleRef(this, _container);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _controller.Stop();
        DestroyWindow(hwnd.Handle);
        _container = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _resizeTimer.Stop();
            _controller.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
