using System.ComponentModel;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

// GPU rendering without a window swap chain. Embedded previews copy their completed image
// into the ordinary GDI child surface, so DXGI never associates/presents against the app HWND.
// Created, used and disposed by the owning renderer on its render thread.
internal sealed partial class GpuPreviewSurface : IDisposable
{
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11Texture2D _staging;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;
    public ID3D11Texture2D Target { get; }

    public GpuPreviewSurface(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        _context = context;
        _width = width;
        _height = height;
        try
        {
            var description = new Texture2DDescription(Format.B8G8R8A8_UNorm,
                (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget);
            Target = device.CreateTexture2D(description);
            description.Usage = ResourceUsage.Staging;
            description.BindFlags = BindFlags.None;
            description.CPUAccessFlags = CpuAccessFlags.Read;
            _staging = device.CreateTexture2D(description);
        }
        catch { Dispose(); throw; }
    }

    public void Present(IntPtr window)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (window == IntPtr.Zero) throw new ArgumentException("A preview window is required.", nameof(window));
        var dc = GetDC(window);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot draw the GPU preview.");
        try { CopyToDeviceContext(dc); }
        finally { _ = ReleaseDC(window, dc); }
    }

    internal void CopyToDeviceContext(IntPtr dc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context.CopyResource(_staging, Target);
        var mapped = _context.Map(_staging, 0, MapMode.Read);
        try
        {
            // D3D rows can contain padding. Describe that physical stride in the DIB header,
            // but blit only the real image width. Negative height keeps the GPU image top-down.
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = checked((int)mapped.RowPitch / 4), Height = -_height,
                    Planes = 1, BitCount = 32
                }
            };
            var copied = StretchDIBits(dc, 0, 0, _width, _height, 0, 0, _width, _height,
                mapped.DataPointer, in info, 0, 0x00CC0020); // DIB_RGB_COLORS, SRCCOPY
            // A hidden preparation surface can legitimately copy zero scanlines.
            if (copied == -1) throw new InvalidOperationException("GPU preview GDI presentation failed.");
        }
        finally { _context.Unmap(_staging, 0); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _staging?.Dispose();
        Target?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetDC(IntPtr window);
    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr dc);
    [LibraryImport("gdi32.dll")]
    private static partial int StretchDIBits(IntPtr dc, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, IntPtr bits,
        in BitmapInfo info, uint usage, uint operation);
}