using System.Runtime.InteropServices;
using System.Text;

namespace AnimatedWallPaper.Services;

internal static partial class DesktopWorker
{
    internal readonly record struct WallpaperTarget(int X, int Y, int Width, int Height,
        string DeviceId = "", string DeviceName = "", uint DpiX = 96, uint DpiY = 96);
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const int WsChild = 0x40000000;
    private const int WsPopup = unchecked((int)0x80000000);
    private static readonly IntPtr HwndBottom = new(1);
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SmtoNormal = 0x0000;
    private static IntPtr _shellViewHandle;
    private static IntPtr _workerHandle;
    private static bool _isRaisedDesktop;

    public static WallpaperTarget[] GetMonitorTargets()
    {
        const int smXVirtualScreen = 76;
        const int smYVirtualScreen = 77;
        var virtualLeft = GetSystemMetrics(smXVirtualScreen);
        var virtualTop = GetSystemMetrics(smYVirtualScreen);
        var targets = System.Windows.Forms.Screen.AllScreens
            .Select(screen =>
            {
                var point = new MonitorPoint
                {
                    X = screen.Bounds.Left + screen.Bounds.Width / 2,
                    Y = screen.Bounds.Top + screen.Bounds.Height / 2
                };
                var monitor = MonitorFromPoint(point, 2);
                if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) != 0) dpiX = dpiY = 96;
                return new WallpaperTarget(
                screen.Bounds.Left - virtualLeft,
                screen.Bounds.Top - virtualTop,
                screen.Bounds.Width,
                screen.Bounds.Height, GetDisplayId(screen.DeviceName), screen.DeviceName, dpiX, dpiY);
            })
            .ToArray();
        AppLog.Write($"Monitor targets={string.Join(";", targets.Select(target => $"{target.X},{target.Y},{target.Width},{target.Height}"))}");
        return targets;
    }

    private static string GetDisplayId(string deviceName)
    {
        var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        return EnumDisplayDevices(deviceName, 0, ref display, 1) && !string.IsNullOrWhiteSpace(display.DeviceId)
            ? display.DeviceId : deviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorPoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(MonitorPoint point, uint flags);
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);

    public static void AttachWallpaperWindow(IntPtr wallpaperHandle, WallpaperTarget? requestedTarget = null,
        bool useLayeredWindow = true)
    {
        AppLog.Write($"AttachWallpaperWindow begin. Wallpaper=0x{wallpaperHandle.ToInt64():X}");
        var desktopHandle = GetDesktopHostHandle();
        if (desktopHandle == IntPtr.Zero) throw new InvalidOperationException("Windows desktop is not ready. HYPNIX will retry recovery.");
        AppLog.Write($"Selected desktop host=0x{desktopHandle.ToInt64():X}; class={GetClassNameText(desktopHandle)}; visible={IsWindowVisible(desktopHandle)}");
        if (desktopHandle != IntPtr.Zero)
        {
            var style = GetWindowLong(wallpaperHandle, GwlStyle);
            Marshal.SetLastPInvokeError(0);
            SetWindowLong(wallpaperHandle, GwlStyle, (style & ~WsPopup) | WsChild);
            AppLog.Write($"Child style applied. Before=0x{style:X8}; after=0x{GetWindowLong(wallpaperHandle, GwlStyle):X8}; " +
                         $"error={Marshal.GetLastPInvokeError()}");

            var exStyle = GetWindowLong(wallpaperHandle, GwlExStyle);
            SetWindowLong(
                wallpaperHandle,
                GwlExStyle,
                exStyle | WsExNoActivate | WsExToolWindow | WsExTransparent |
                (_isRaisedDesktop && useLayeredWindow ? WsExLayered : 0));
            AppLog.Write($"Extended style requested. Before=0x{exStyle:X8}; after=0x{GetWindowLong(wallpaperHandle, GwlExStyle):X8}");

            if (_isRaisedDesktop && useLayeredWindow)
            {
                Marshal.SetLastPInvokeError(0);
                var alphaResult = SetLayeredWindowAttributes(wallpaperHandle, 0, 255, LwaAlpha);
                AppLog.Write($"Layered alpha applied. result={alphaResult}; error={Marshal.GetLastPInvokeError()}");
            }

            Marshal.SetLastPInvokeError(0);
            var previousParent = SetParent(wallpaperHandle, desktopHandle);
            if (GetParent(wallpaperHandle) != desktopHandle)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The wallpaper could not attach to Explorer.");
            AppLog.Write($"SetParent previous=0x{previousParent.ToInt64():X}; error={Marshal.GetLastPInvokeError()}; " +
                         $"current=0x{GetParent(wallpaperHandle).ToInt64():X}");

            AppLog.Write($"Layered child state after SetParent: exStyle=0x{GetWindowLong(wallpaperHandle, GwlExStyle):X8}");

            if (GetClientRect(desktopHandle, out var desktopBounds))
            {
                var target = requestedTarget ?? new WallpaperTarget(
                    0, 0,
                    desktopBounds.Right - desktopBounds.Left,
                    desktopBounds.Bottom - desktopBounds.Top);
                Marshal.SetLastPInvokeError(0);
                var positioned = SetWindowPos(
                    wallpaperHandle,
                    _isRaisedDesktop ? _shellViewHandle : HwndBottom,
                    target.X,
                    target.Y,
                    target.Width,
                    target.Height,
                    SwpNoActivate | SwpFrameChanged);
                if (!positioned) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The wallpaper could not be placed on the desktop.");
                AppLog.Write($"SetWindowPos result={positioned}; error={Marshal.GetLastPInvokeError()}; " +
                             $"hostClient={desktopBounds.Left},{desktopBounds.Top},{desktopBounds.Right},{desktopBounds.Bottom}; " +
                             $"currentParent=0x{GetParent(wallpaperHandle).ToInt64():X}");

                if (_isRaisedDesktop && _workerHandle != IntPtr.Zero)
                {
                    var workerPositioned = SetWindowPos(
                        _workerHandle,
                        HwndBottom,
                        0,
                        0,
                        0,
                        0,
                        0x0001 | 0x0002 | SwpNoActivate);
                    AppLog.Write($"WorkerW moved to bottom. handle=0x{_workerHandle.ToInt64():X}; result={workerPositioned}; " +
                                 $"error={Marshal.GetLastPInvokeError()}");
                }
            }
            else
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "The desktop bounds are not available.");
            }
        }

        AppLog.Write($"Attach complete. Wallpaper visible={IsWindowVisible(wallpaperHandle)}; rect={GetRectText(wallpaperHandle)}; " +
                     $"parent=0x{GetParent(wallpaperHandle).ToInt64():X}");
    }

    private static IntPtr GetDesktopHostHandle()
    {
        var progman = FindWindow("Progman", null);
        AppLog.Write($"Progman=0x{progman.ToInt64():X}");
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Marshal.SetLastPInvokeError(0);
        var progmanExStyle = GetWindowLong(progman, GwlExStyle);
        _isRaisedDesktop = (progmanExStyle & WsExNoRedirectionBitmap) != 0;
        AppLog.Write($"Progman exStyle=0x{progmanExStyle:X8}; raisedDesktop={_isRaisedDesktop}");

        var messageResult = SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SmtoNormal, 1000, out var messageValue);
        AppLog.Write($"Progman 0x052C result=0x{messageResult.ToInt64():X}; value=0x{messageValue.ToInt64():X}; error={Marshal.GetLastPInvokeError()}");

        var desktopHost = IntPtr.Zero;
        _shellViewHandle = IntPtr.Zero;
        _workerHandle = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            var className = GetClassNameText(topHandle);
            if (className is "WorkerW" or "Progman")
            {
                AppLog.Write($"Desktop candidate=0x{topHandle.ToInt64():X}; class={className}; visible={IsWindowVisible(topHandle)}; " +
                             $"rect={GetRectText(topHandle)}; shellView=0x{shellView.ToInt64():X}");
            }
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            desktopHost = topHandle;
            _shellViewHandle = shellView;
            return false;
        }, IntPtr.Zero);

        if (_isRaisedDesktop)
        {
            desktopHost = progman;
            _shellViewHandle = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            _workerHandle = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            AppLog.Write($"Raised desktop children: ShellView=0x{_shellViewHandle.ToInt64():X}; " +
                         $"WorkerW=0x{_workerHandle.ToInt64():X}; WorkerW visible={IsWindowVisible(_workerHandle)}; " +
                         $"WorkerW rect={GetRectText(_workerHandle)}");
        }

        return desktopHost != IntPtr.Zero ? desktopHost : progman;
    }

    private static string GetClassNameText(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return "none";
        }

        var value = new StringBuilder(256);
        return GetClassName(handle, value, value.Capacity) > 0 ? value.ToString() : "unknown";
    }

    private static string GetRectText(IntPtr handle)
    {
        return GetWindowRect(handle, out var rect)
            ? $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}"
            : $"unavailable(error={Marshal.GetLastPInvokeError()})";
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetParent(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);
}
