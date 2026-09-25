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
            if (args.Contains("--lemon-remote"))
            {
                LemonRemoteChecks.RunAsync(output).GetAwaiter().GetResult();
                return 0;
            }
            if (args.Contains("--audio-probe"))
            {
                EventHorizonRenderChecks.ProbeLiveAudio(output);
                return 0;
            }
            var app = new System.Windows.Application();
            var resourceXml = System.Xml.Linq.XDocument.Load("App.xaml").Root!
                .Element(System.Xml.Linq.XName.Get("Application.Resources", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))!;
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                string.Concat(resourceXml.Nodes()) + "</ResourceDictionary>");
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (args.Contains("--layered-child")) { LayeredChildWindowChecks.Run(output); return 0; }
            if (args.Contains("--gdi-present")) { GdiPresentChecks.Run(output); return 0; }
            if (args.Contains("--show-product-preview"))
            {
                // Isolated presentation fixture; real gallery and GPU previews, no commerce requests.
                var previewSettings = new AppSettingsStore(Path.Combine(output, "product-preview-settings.json"));
                previewSettings.Save(new AppSettings { SelectedWallpaperId = "event-horizon", AudioReactive = false,
                    FramesPerSecond = 30, PreviewPaneCollapsed = true });
                var productWindow = new MainWindow(previewSettings, Path.Combine(output, "product-library"),
                    license: LicenseWindowChecks.CreateOwnedLicense()) { Width = 1440, Height = 900, Title = "HYPNIX" };
                productWindow.Loaded += (_, _) => { productWindow.Width = 1280; productWindow.Height = 1040; };
                app.Run(productWindow);
                return 0;
            }
            if (args.Contains("--microphone-probe"))
            {
                MicrophoneAudioChecks.Probe(output);
                return 0;
            }
            if (args.Contains("--lemon-license")) { LemonLicenseWindowChecks.Run(output); return 0; }
            if (args.Contains("--license-only")) { LicenseWindowChecks.Run(output); return 0; }
            if (args.Contains("--show-license-demo")) { LicenseWindowChecks.ShowDemo(app, output); return 0; }
            if (args.Contains("--displays-ui")) { DisplayWindowChecks.Run(output); return 0; }
            if (args.Contains("--multi-preview")) { MultiPreviewChecks.Run(output); return 0; }
            if (args.Contains("--microphone-ui")) { MicrophoneWindowChecks.Run(output); return 0; }
            if (args.Contains("--displays-desktop")) { DisplayDesktopChecks.Run(output, args.ElementAtOrDefault(2), args.ElementAtOrDefault(3)); return 0; }
            if (args.Contains("--capture-thumbnails"))
            {
                var surface = CreateWindowEx(0, "STATIC", "HYPNIX thumbnail surface", 0x80000000,
                    0, 0, 960, 540, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (surface == IntPtr.Zero) throw new InvalidOperationException("Thumbnail render surface could not be created.");
                try { ThumbnailCapture.Run(surface, output); }
                finally { DestroyWindow(surface); }
                return 0;
            }
            if(args.Contains("--settings-only")){SettingsWindowChecks.Run(output);return 0;}
            if(args.Contains("--layout-only")){SettingsWindowChecks.Run(output);LibraryLayoutChecks.Run(output);return 0;}
            SettingsWindowChecks.Run(output);
            if (args.Contains("--show-fire-gallery"))
            {
                var reviewSettings=new AppSettingsStore(Path.Combine(output,"gallery-review-settings.json"));
                reviewSettings.Save(new AppSettings {SelectedWallpaperId="living-fire"});
                var reviewWindow=new MainWindow(reviewSettings,Path.Combine(output,"gallery-review-library"), license: LicenseWindowChecks.CreateOwnedLicense());
                app.Run(reviewWindow);
                return 0;
            }
            var parent = CreateWindowEx(0, "STATIC", "HYPNIX hidden test surface", 0x80000000,
                0, 0, 640, 360, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (parent == IntPtr.Zero) throw new InvalidOperationException("Hidden native test surface could not be created.");
            try
            {
                if (args.Contains("--paused-reveal"))
                {
                    PausedWallpaperRevealChecks.Run(parent, output);
                    return 0;
                }
                if (args.Contains("--microphone-lifecycle"))
                {
                    MicrophoneLifecycleChecks.Run(parent, output);
                    return 0;
                }
                if (args.Contains("--event-horizon-only"))
                {
                    EventHorizonRenderChecks.Run(parent, output);
                    EventHorizonRenderChecks.CaptureReviewFrames(parent, output);
                    return 0;
                }
                if (args.Contains("--spectral-only"))
                {
                    SpectralBloomRenderChecks.Run(parent, output);
                    return 0;
                }
                if (args.Contains("--fire-ultrawide")) {FireUltrawideChecks.Run(parent,output);return 0;}
                if (args.Contains("--fire-only")) {FireRenderChecks.Run(parent,output);return 0;}
                FireRenderChecks.Run(parent,output);
                FireUltrawideChecks.Run(parent,output);
                NeonRibbonsRenderChecks.Run(parent, output);
                NeonRibbonsRenderChecks.Run(parent, output, "LiquidOrbs.hlsl", "liquid-orbs");
                NeonRibbonsRenderChecks.Run(parent, output, "FractalPyramid.hlsl", "fractal-pyramid");
                NeonRibbonsRenderChecks.Run(parent, output, "Kaleidoscope.hlsl", "kaleidoscope");
                NeonRibbonsRenderChecks.Run(parent, output, "Lotus.hlsl", "lotus");
                SpectralBloomRenderChecks.Run(parent, output);
                EventHorizonRenderChecks.Run(parent, output);
                AethelisRenderChecks.Run(parent, output);
                foreach (var mode in new[] { NativeRenderMode.Ambient, NativeRenderMode.VisualizerDemo,
                    NativeRenderMode.AethelisVisualizer, NativeRenderMode.AethelisFlameBurst, NativeRenderMode.FlamethrowerRingV2,
                    NativeRenderMode.SpectralBloom, NativeRenderMode.NeonRibbons, NativeRenderMode.LiquidOrbs,
                    NativeRenderMode.EventHorizon, NativeRenderMode.FractalPyramid, NativeRenderMode.Kaleidoscope,
                    NativeRenderMode.Lotus, NativeRenderMode.LivingFire })
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

            var window = new MainWindow(new AppSettingsStore(Path.Combine(output, "settings.json")), Path.Combine(output, "library"), license: LicenseWindowChecks.CreateOwnedLicense());
            new WindowInteropHelper(window).EnsureHandle();
            var gallery = (System.Windows.Controls.ListBox)window.FindName("WallpaperGallery");
            if (gallery.Items.Count < 5) throw new InvalidOperationException("Built-in catalog was not packaged correctly.");
            foreach (var kind in new[] { WallpaperKind.BuiltIn, WallpaperKind.VisualizerDemo,
                WallpaperKind.AethelisVisualizer, WallpaperKind.AethelisFlameBurst, WallpaperKind.FlamethrowerRingV2,
                WallpaperKind.SpectralBloom, WallpaperKind.NeonRibbons, WallpaperKind.LiquidOrbs, WallpaperKind.EventHorizon,
                WallpaperKind.FractalPyramid, WallpaperKind.Kaleidoscope, WallpaperKind.Lotus, WallpaperKind.LivingFire })
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

            // The dedicated visualizer settings window: verify its XAML loads and the size/position
            // controls round-trip through the preferences (it is opened on demand in the real app).
            var settingsWindow = new VisualizerSettingsWindow();
            settingsWindow.SetWallpaper(gallery.Items.OfType<WallpaperEntry>().Single(e => e.Kind == WallpaperKind.FractalPyramid));
            var layoutControls = (FrameworkElement)settingsWindow.FindName("LayoutControls");
            var colorsCard = (FrameworkElement)settingsWindow.FindName("ColorsCard");
            var glowControls = (FrameworkElement)settingsWindow.FindName("GlowControls");
            // Switch through every visualizer in one existing window, as gallery selection does.
            // The window must advertise exactly the controls each renderer actually honors, so
            // capabilities are the single source of truth (no control shown that does nothing).
            foreach (var entry in gallery.Items.OfType<WallpaperEntry>().Where(e => e.IsVisualizer))
            {
                settingsWindow.SetWallpaper(entry);
                if ((layoutControls.Visibility == Visibility.Visible) != entry.SupportsLayoutControls)
                    throw new InvalidOperationException($"Incorrect layout controls for {entry.Kind}.");
                if ((colorsCard.Visibility == Visibility.Visible) != entry.SupportsColorTheme)
                    throw new InvalidOperationException($"Incorrect color palette visibility for {entry.Kind}.");
                if ((glowControls.Visibility == Visibility.Visible) != entry.SupportsGlow)
                    throw new InvalidOperationException($"Incorrect glow control visibility for {entry.Kind}.");
            }
            settingsWindow.LoadValues(new VisualizerPreferences(3f, 4f, 1.2f, 2, 1.5f, 0.25f, -0.35f));
            var roundTrip = settingsWindow.ReadValues();
            if (Math.Abs(roundTrip.Scale - 1.5f) > 0.001f || Math.Abs(roundTrip.OffsetX - 0.25f) > 0.001f
                || Math.Abs(roundTrip.OffsetY + 0.35f) > 0.001f || roundTrip.ColorTheme != 2)
                throw new InvalidOperationException("Visualizer settings window did not round-trip size/position/color.");
            settingsWindow.Close();
            Console.WriteLine("PASS: visualizer settings window loads and round-trips size/position/color");

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
