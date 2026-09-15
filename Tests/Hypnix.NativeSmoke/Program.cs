using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/native-smoke");
        Directory.CreateDirectory(output);
        try
        {
            var app = new System.Windows.Application();
            var resourceXml = System.Xml.Linq.XDocument.Load("App.xaml").Root!
                .Element(System.Xml.Linq.XName.Get("Application.Resources", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))!;
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                string.Concat(resourceXml.Nodes()) + "</ResourceDictionary>");
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var parent = CreateWindowEx(0, "STATIC", "HYPNIX hidden test surface", 0x80000000,
                0, 0, 640, 360, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Hidden native test surface could not be created.");
            try
            {
                if (args.Contains("--spectral-only"))
                {
                    SpectralBloomRenderChecks.Run(parent, output);
                    return 0;
                }
                NeonRibbonsRenderChecks.Run(parent, output);
                NeonRibbonsRenderChecks.Run(parent, output, "LiquidOrbs.hlsl", "liquid-orbs");
                SpectralBloomRenderChecks.Run(parent, output);
                EventHorizonRenderChecks.Run(parent, output);
                foreach (var mode in new[] { NativeRenderMode.Ambient, NativeRenderMode.VisualizerDemo,
                    NativeRenderMode.AethelisVisualizer, NativeRenderMode.AethelisFlameBurst, NativeRenderMode.FlamethrowerRingV2,
                    NativeRenderMode.SpectralBloom, NativeRenderMode.NeonRibbons, NativeRenderMode.LiquidOrbs,
                    NativeRenderMode.EventHorizon })
                {
                    using var host = new NativeWallpaperHost(mode, preview: new PreviewTarget(parent, 640, 360));
                    host.Start(30, reveal: false);
                    if (!host.IsHealthy) throw new InvalidOperationException($"{mode} did not prepare.");
                    host.Show();
                    host.SubmitAudioBands(Enumerable.Repeat(0.4f, 64).ToArray());
                    host.UpdateVisualizerSettings(VisualizerSettings.Default);
                    host.UpdateVisualizerSettings(VisualizerSettings.Default with { Intensity = 8, Sensitivity = 12, Glow = 3 });
                    host.Pause();
                    host.SetFrameCap(15);
                    host.Resume();
                    var handle = host.Handle;
                    host.Dispose();
                    if (IsWindow(handle)) throw new InvalidOperationException($"{mode} leaked its host.");
                    Console.WriteLine($"PASS: {mode} prepare/render/pause/resume/dispose");
                }
            }
            finally { DestroyWindow(parent); }

            if (args.Length >= 3)
            {
                var mediaDirectory = Path.GetFullPath(args[1]);
                var videoPath = Path.GetFullPath(args[2]);
                parent = CreateWindowEx(0, "STATIC", "HYPNIX hidden video test", 0x80000000,
                    0, 0, 320, 240, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    using var session = RunOnDispatcher(() => VideoWallpaperSession.CreateAsync(
                        new WallpaperRequest("test-video", WallpaperKind.ExampleVideo, 15, videoPath,
                            mediaDirectory, Preview: new PreviewTarget(parent, 320, 240)), CancellationToken.None));
                    if (!session.IsHealthy) throw new InvalidOperationException("Video did not produce a first frame.");
                    session.Show();
                    session.Pause();
                    RunOnDispatcher(async () => { await Task.Delay(250); return true; });
                    session.Resume();
                    RunOnDispatcher(async () => { await Task.Delay(250); return true; });
                    if (!session.IsHealthy) throw new InvalidOperationException("Video failed after pause/resume.");
                    session.Dispose();
                    Console.WriteLine("PASS: real FFmpeg probe/first-frame/15 FPS/pause/resume/decoder shutdown");
                }
                finally { DestroyWindow(parent); }
            }

            var window = new MainWindow(new AppSettingsStore(Path.Combine(output, "settings.json")), Path.Combine(output, "library"));
            new WindowInteropHelper(window).EnsureHandle();
            var gallery = (System.Windows.Controls.ListBox)window.FindName("WallpaperGallery");
            if (gallery.Items.Count < 5) throw new InvalidOperationException("Built-in catalog was not packaged correctly.");
            foreach (var kind in new[] { WallpaperKind.BuiltIn, WallpaperKind.VisualizerDemo,
                WallpaperKind.AethelisVisualizer, WallpaperKind.AethelisFlameBurst, WallpaperKind.FlamethrowerRingV2,
                WallpaperKind.SpectralBloom, WallpaperKind.NeonRibbons, WallpaperKind.LiquidOrbs, WallpaperKind.EventHorizon })
            {
                if (!gallery.Items.OfType<WallpaperEntry>().Any(entry => entry.Kind == kind))
                    throw new InvalidOperationException($"Built-in wallpaper missing from the gallery: {kind}");
            }
            var root = (FrameworkElement)window.Content;
            foreach (var size in new[] { new System.Windows.Size(1132, 652), new System.Windows.Size(752, 492) })
            {
                root.Measure(size);
                root.Arrange(new Rect(size));
                root.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"shell-{size.Width}.png"));
                png.Save(file);
            }
            Console.WriteLine($"PASS: shell construction and layout; {gallery.Items.Count} manifest-driven cards");
            typeof(MainWindow).GetField("_isQuitting", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
            window.Close();
            Console.WriteLine("PASS: shell shutdown");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static T RunOnDispatcher<T>(Func<Task<T>> operation)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var result = new TaskCompletionSource<T>();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try { result.SetResult(await operation()); }
            catch (Exception exception) { result.SetException(exception); }
            finally { frame.Continue = false; }
        }));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        return result.Task.GetAwaiter().GetResult();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
}
