using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using AnimatedWallPaper.Services;

internal static class PreviewPresentationChecks
{
    // An odd, non-aligned width exercises D3D row padding and GDI's top-down image layout.
    private const int Width = 213, Height = 121;

    public static void Run(string output)
    {
        var audio = AethelisAudioProfile.Analyze(Enumerable.Repeat(0.2f, 64).ToArray(), 1);
        using (var renderer = new AethelisGpuRenderer(IntPtr.Zero, Width, Height, "NeonRibbons.hlsl", preview: true))
        {
            var surface = Surface(renderer, "_previewSurface", "_swapChain");
            CheckFrames(surface, time =>
            {
                renderer.BeginFrame();
                renderer.RenderViewport(0, 0, Width, Height, time, audio, VisualizerSettings.Default);
                return SpectralBloomRenderChecks.ReadBack(renderer);
            }, output, "shader-preview");
        }
        using (var renderer = new FireGpuRenderer(IntPtr.Zero, Width, Height, preview: true))
        {
            var surface = Surface(renderer, "previewSurface", "swap");
            CheckFrames(surface, time =>
            {
                renderer.BeginFrame();
                renderer.RenderViewport(0, 0, Width, Height, time, audio, VisualizerSettings.Default);
                return renderer.Pixels();
            }, output, "fire-preview");
        }
        Console.WriteLine("PASS: GPU previews render without an HWND or swap chain; GDI pixels match GPU RGB at every pixel across animated frames (213x121).");
    }

    private static GpuPreviewSurface Surface(object renderer, string surfaceName, string swapName)
    {
        object? Field(string name) => renderer.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
        if (Field(swapName) is not null) throw new Exception("Preview created a window swap chain.");
        return (GpuPreviewSurface)Field(surfaceName)!;
    }

    private static void CheckFrames(GpuPreviewSurface surface, Func<double, byte[]> render, string output, string name)
    {
        byte[]? previous = null;
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        foreach (var time in new[] { 8d, 8d + 1d / 30 })
        {
            var expected = render(time);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Magenta);
                var dc = graphics.GetHdc();
                try { surface.CopyToDeviceContext(dc); }
                finally { graphics.ReleaseHdc(dc); }
            }
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var actual = bitmap.GetPixel(x, y);
                var offset = (y * Width + x) * 4;
                if (actual.B != expected[offset] || actual.G != expected[offset + 1] || actual.R != expected[offset + 2])
                    throw new Exception($"{name}: GDI pixel differs from GPU at ({x},{y}).");
            }
            if (previous is not null && expected.SequenceEqual(previous)) throw new Exception($"{name}: preview stopped animating.");
            previous = expected;
            bitmap.Save(Path.Combine(output, $"{name}-{time}.png"), ImageFormat.Png);
        }
    }
}