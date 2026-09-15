using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class SpectralBloomRenderChecks
{
    private const int Width = 640;
    private const int Height = 360;

    public static void Run(IntPtr window, string output)
    {
        var silence = Render(window, 30, false);
        Save(silence, Path.Combine(output, "spectral-silence.png"));
        var means = new List<double>();
        foreach (var fps in new[] { 15, 30, 60 })
        {
            var pixels = Render(window, fps, true);
            Save(pixels, Path.Combine(output, $"spectral-{fps}fps.png"));
            var mean = Mean(pixels);
            means.Add(mean);
            if (mean < 1 || mean > 95) throw new Exception($"Spectral Bloom exposure out of range: {mean:F2}");
            Console.WriteLine($"PASS: Spectral Bloom {fps} FPS; mean channel={mean:F2}");
        }
        if (means.Max() / means.Min() > 1.3) throw new Exception("Spectral Bloom exposure varies excessively with FPS.");
        if (means[1] < Mean(silence) * 1.15) throw new Exception("Spectral Bloom is not responding to audio energy.");

        CheckIndependentFreeze(window, output);

        var reversed = Render(window, 30, true, reverse: true);
        var forward = Render(window, 30, true);
        if (forward.SequenceEqual(reversed)) throw new Exception("Individual FFT bands do not affect the image.");
        Console.WriteLine("PASS: FFT distribution changes the image with identical grouped audio profile");
    }

    private static void CheckIndependentFreeze(IntPtr window, string output)
    {
        // Dispose this swap chain before Render creates another for the same HWND.
        using var renderer = new AethelisGpuRenderer(window, Width, Height, "SpectralBloom.hlsl");
        var bands = Enumerable.Range(0, 64).Select(i => i % 4 == 0 ? 0.9f : 0.1f).ToArray();
        var profile = AethelisAudioProfile.Analyze(bands, 1);
        for (var n = 0; n <= 90; n++)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 320, 360, n / 30.0, profile, VisualizerSettings.Default, spectrum: bands);
            renderer.RenderViewport(320, 0, 320, 360, n / 30.0, profile, VisualizerSettings.Default, spectrum: bands);
        }
        var before = ReadBack(renderer);
        for (var n = 91; n <= 120; n++)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 320, 360, n / 30.0, profile, VisualizerSettings.Default, spectrum: bands);
            renderer.RenderViewport(320, 0, 320, 360, 3, profile, VisualizerSettings.Default, true, bands);
        }
        var after = ReadBack(renderer);
        var movingDifference = 0L;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        for (var c = 0; c < 3; c++)
        {
            var i = (y * Width + x) * 4 + c;
            if (x >= 320 && before[i] != after[i]) throw new Exception("Frozen monitor changed pixels.");
            if (x < 320) movingDifference += Math.Abs(before[i] - after[i]);
        }
        if (movingDifference == 0) throw new Exception("Active monitor stopped with frozen monitor.");
        Save(after, Path.Combine(output, "spectral-independent-freeze.png"));
        Console.WriteLine("PASS: independent viewport freeze is pixel-exact; adjacent viewport continues moving");

    }

    private static byte[] Render(IntPtr window, int fps, bool audio, bool reverse = false)
    {
        using var renderer = new AethelisGpuRenderer(window, Width, Height, "SpectralBloom.hlsl");
        for (var n = 0; n <= fps * 4; n++)
        {
            var time = n / (double)fps;
            var bands = Enumerable.Range(0, 64).Select(i => audio
                ? (float)(0.12 + 0.65 * Math.Pow(0.5 + 0.5 * Math.Sin(i * 0.37 + time * 2.1), 3)) : 0).ToArray();
            var profile = AethelisAudioProfile.Analyze(bands, 1);
            if (reverse) Array.Reverse(bands); // Same profile; only the detailed spectrum differs.
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, Width, Height, time, profile, VisualizerSettings.Default, spectrum: bands);
        }
        return ReadBack(renderer);
    }

    // Read the renderer's own GPU target, without desktop icons, UI or screen capture.
    internal static byte[] ReadBack(AethelisGpuRenderer renderer)
    {
        T Field<T>(string name) => (T)typeof(AethelisGpuRenderer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        var device = Field<ID3D11Device>("_device");
        var context = Field<ID3D11DeviceContext>("_context");
        var source = Field<ID3D11Texture2D>("_backBuffer");
        var description = source.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = device.CreateTexture2D(description);
        context.CopyResource(staging, source);
        var mapped = context.Map(staging, 0, MapMode.Read);
        var pixels = new byte[Width * Height * 4];
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, pixels, y * Width * 4, Width * 4);
        }
        finally { context.Unmap(staging, 0); }
        return pixels;
    }

    private static double Mean(byte[] pixels)
    {
        long sum = 0;
        for (var i = 0; i < pixels.Length; i += 4) sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
        return sum / (Width * Height * 3.0);
    }

    internal static void Save(byte[] pixels, string path)
    {
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
