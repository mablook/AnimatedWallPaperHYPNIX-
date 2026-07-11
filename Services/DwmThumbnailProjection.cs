using System.Runtime.InteropServices;

namespace AnimatedWallPaper.Services;

internal sealed partial class DwmThumbnailProjection : IDisposable
{
    private const uint Destination = 0x00000001;
    private const uint Opacity = 0x00000004;
    private const uint Visible = 0x00000008;
    private IntPtr _thumbnail;

    public DwmThumbnailProjection(IntPtr source, IntPtr destination)
    {
        var result = DwmRegisterThumbnail(destination, source, out _thumbnail);
        AppLog.Write($"DwmRegisterThumbnail source=0x{source.ToInt64():X}; destination=0x{destination.ToInt64():X}; " +
                     $"thumbnail=0x{_thumbnail.ToInt64():X}; result=0x{result:X8}");

        if (result != 0 || _thumbnail == IntPtr.Zero)
        {
            throw new InvalidOperationException($"DWM thumbnail registration failed: 0x{result:X8}");
        }
    }

    public void FillDestination(int width, int height)
    {
        var properties = new ThumbnailProperties
        {
            Flags = Destination | Opacity | Visible,
            Destination = new NativeRect { Left = 0, Top = 0, Right = width, Bottom = height },
            Opacity = 255,
            IsVisible = true,
            SourceClientAreaOnly = true
        };

        var result = DwmUpdateThumbnailProperties(_thumbnail, ref properties);
        AppLog.Write($"DwmUpdateThumbnailProperties size={width}x{height}; result=0x{result:X8}");
        if (result != 0)
        {
            throw new InvalidOperationException($"DWM thumbnail update failed: 0x{result:X8}");
        }
    }

    public void Dispose()
    {
        if (_thumbnail == IntPtr.Zero)
        {
            return;
        }

        var result = DwmUnregisterThumbnail(_thumbnail);
        AppLog.Write($"DwmUnregisterThumbnail result=0x{result:X8}");
        _thumbnail = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool IsVisible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmRegisterThumbnail(IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail, ref ThumbnailProperties properties);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmUnregisterThumbnail(IntPtr thumbnail);
}
