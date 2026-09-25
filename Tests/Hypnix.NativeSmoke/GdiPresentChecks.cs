using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnimatedWallPaper.Services;

// Verifies that the GDI wallpapers' GPU presentation path (GdiWallpaperSwapChain) actually puts the
// System.Drawing frame onto its swap-chain surface. This is the mechanism that fixes the black
// Built-in ambient / Audio Visualizer on the Windows 11 raised desktop. It renders a two-colour
// bitmap through the swap chain and reads the back buffer back, so it needs Direct3D 11 but no live
// desktop, Explorer or audio device. The window it uses is an owned, hidden STATIC surface.
internal static class GdiPresentChecks
{
    private const int Width = 320;
    private const int Height = 180;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var window = CreateWindowEx(0, "STATIC", "HYPNIX gdi-present surface", 0x80000000,
            0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (window == IntPtr.Zero) throw new InvalidOperationException("Hidden test surface could not be created.");
        try
        {
            using var swapChain = new GdiWallpaperSwapChain(window, Width, Height);
            using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            var top = Color.FromArgb(255, 200, 30, 40);      // red upper half
            var bottom = Color.FromArgb(255, 40, 60, 200);   // blue lower half
            using (var graphics = Graphics.FromImage(bitmap))
            {
                using var topBrush = new SolidBrush(top);
                using var bottomBrush = new SolidBrush(bottom);
                graphics.FillRectangle(topBrush, 0, 0, Width, Height / 2);
                graphics.FillRectangle(bottomBrush, 0, Height / 2, Width, Height - Height / 2);
            }

            // A first present exercises the real Present path (Draw + swap). The capture variant then
            // draws again and reads the back buffer, which holds the composited frame before flipping.
            swapChain.Present(bitmap);
            var pixels = swapChain.CaptureForTest(bitmap);

            var (tb, tg, tr) = Sample(pixels, Width / 2, Height / 4);
            var (bb, bg, br) = Sample(pixels, Width / 2, Height * 3 / 4);
            AssertClose("upper red", tr, tg, tb, top);
            AssertClose("lower blue", br, bg, bb, bottom);
            var distinct = tr != br || tg != bg || tb != bb;
            if (!distinct) throw new InvalidOperationException("The two halves were not distinguishable after presentation.");

            SaveBitmap(bitmap, Path.Combine(output, "gdi-present-source.png"));
            File.WriteAllText(Path.Combine(output, "gdi-present.json"), JsonSerializer.Serialize(new
            {
                Size = $"{Width}x{Height}",
                UpperReadBgr = new { B = tb, G = tg, R = tr },
                LowerReadBgr = new { B = bb, G = bg, R = br },
                Distinct = distinct
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: GdiWallpaperSwapChain presents the GDI frame to its swap-chain surface " +
                              $"(upper R={tr},G={tg},B={tb}; lower R={br},G={bg},B={bb}); no desktop or audio device used.");
        }
        finally { DestroyWindow(window); }
    }

    private static (int B, int G, int R) Sample(byte[] pixels, int x, int y)
    {
        var offset = (y * Width + x) * 4; // B8G8R8A8 read-back: B, G, R, A
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static void AssertClose(string label, int r, int g, int b, Color expected)
    {
        if (Math.Abs(r - expected.R) > 6 || Math.Abs(g - expected.G) > 6 || Math.Abs(b - expected.B) > 6)
            throw new InvalidOperationException(
                $"{label}: presented color R={r},G={g},B={b} does not match expected R={expected.R},G={expected.G},B={expected.B}.");
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        using var file = File.Create(path);
        bitmap.Save(file, ImageFormat.Png);
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
