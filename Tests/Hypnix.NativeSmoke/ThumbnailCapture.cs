using System.IO;
using System.Reflection;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper.Services;

// Captures the production preview renderers, without desktop, chrome or UI overlays.
// Fixed animation time and a quiet synthetic spectrum make regeneration reproducible.
internal static class ThumbnailCapture
{
    private const int Width = 960, Height = 540;
    public static void Run(IntPtr parent, string output)
    {
        var entries = WallpaperCatalog.LoadBuiltIns(Path.GetFullPath("Assets/Wallpapers"));
        foreach (var entry in entries)
        {
            var settings = (entry.Defaults ?? new()).ToSettings();
            var path = Path.Combine(output, entry.Id + ".png");
            if (entry.Kind is WallpaperKind.BuiltIn or WallpaperKind.VisualizerDemo)
            {
                using var host = new NativeWallpaperHost(entry.Kind == WallpaperKind.BuiltIn ? NativeRenderMode.Ambient : NativeRenderMode.VisualizerDemo,
                    customBackground: entry.BackgroundPath, preview: new PreviewTarget(parent, Width, Height));
                host.UpdateVisualizerSettings(settings);
                using var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var method = entry.Kind == WallpaperKind.BuiltIn ? "RenderAmbient" : "RenderVisualizer";
                    object[] args = entry.Kind == WallpaperKind.BuiltIn ? [graphics, Width, Height, 6d] : [graphics, Width, Height, 6d, Bands(6)];
                    typeof(NativeWallpaperHost).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, args);
                }
                bitmap.Save(path, ImageFormat.Png);
            }
            else if (entry.Kind == WallpaperKind.LivingFire)
            {
                using var renderer = new FireGpuRenderer(parent, Width, Height);
                for (var frame = 0; frame <= 180; frame++)
                {
                    var time = frame / 30d; var bands = Bands(time);
                    renderer.BeginFrame();
                    renderer.RenderViewport(0, 0, Width, Height, time, AethelisAudioProfile.Analyze(bands, settings.Sensitivity), settings, spectrum: bands);
                }
                SpectralBloomRenderChecks.Save(renderer.Pixels(), path, Width, Height);
            }
            else
            {
                var shader = entry.Kind switch {
                    WallpaperKind.AethelisVisualizer => "Aethelis", WallpaperKind.AethelisFlameBurst => "AethelisFlameBurst",
                    _ => entry.Kind.ToString()
                };
                using var renderer = new AethelisGpuRenderer(parent, Width, Height, shader + ".hlsl");
                var lastFrame = entry.Kind == WallpaperKind.FlamethrowerRingV2 ? 90 : 180;
                for (var frame = 0; frame <= lastFrame; frame++)
                {
                    var time = frame / 30d; var bands = Bands(time);
                    renderer.BeginFrame();
                    renderer.RenderViewport(0, 0, Width, Height, time, AethelisAudioProfile.Analyze(bands, settings.Sensitivity), settings, spectrum: bands);
                }
                SpectralBloomRenderChecks.Save(SpectralBloomRenderChecks.ReadBack(renderer), path, Width, Height);
            }
            // Preserve manifest paths and format; replacing thumbnails never changes the live preview.
            using var input = File.OpenRead(path);
            var image = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapEncoder encoder = Path.GetExtension(entry.PreviewPath!).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder { QualityLevel = 94 } : new PngBitmapEncoder();
            encoder.Frames.Add(image);
            using var destination = File.Create(entry.PreviewPath!);
            encoder.Save(destination);
            Console.WriteLine($"CAPTURE: {entry.Title} · {Width} × {Height} · {entry.PreviewPath}");
        }
    }
    private static float[] Bands(double time) => Enumerable.Range(0, 64)
        .Select(i => (float)(.015 + .05 * Math.Pow(.5 + .5 * Math.Sin(i * .37 + time * 2.1), 3))).ToArray();
}
