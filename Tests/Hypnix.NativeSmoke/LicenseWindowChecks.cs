using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

internal static class LicenseWindowChecks
{
    // This provider lives only in the test executable, which is excluded from distribution.
    private sealed class DemoProvider : IAppLicenseProvider
    {
        public bool IsStoreManaged => true;
        public AppLicenseSnapshot Current = new(AppLicenseKind.Trial, DateTimeOffset.UtcNow.AddDays(15));
        public bool Offline;
        public event Action? LicenseChanged;
        public void Initialize(IntPtr owner) { }
        public Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken token) => Offline
            ? Task.FromException<AppLicenseSnapshot>(new IOException("Simulated Store outage")) : Task.FromResult(Current);
        public Task<string?> GetPriceAsync(CancellationToken token) => Task.FromResult<string?>("4,99 €");
        public Task<AppPurchaseResult> PurchaseAsync(CancellationToken token)
        {
            Current = new(AppLicenseKind.Owned);
            return Task.FromResult(AppPurchaseResult.Purchased);
        }
        public void Set(AppLicenseKind kind, int days = 15)
        {
            Current = new(kind, kind == AppLicenseKind.Trial ? DateTimeOffset.UtcNow.AddDays(days) : null);
            LicenseChanged?.Invoke();
        }
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Session : IWallpaperSession
    {
        public bool Disposed;
        public int? ProcessId => null;
        public bool IsHealthy => !Disposed;
        public void Show() { }
        public void SetFrameCap(int value) { }
        public void Resume() { }
        public void Pause() { }
        public void SetPausedMonitors(IReadOnlyList<int> indices) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
        public void SetAudioEnabled(bool value) { }
        public void Dispose() => Disposed = true;
    }
    private static void Invoke(MainWindow window, string name) => typeof(MainWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
    private static void Close(MainWindow window)
    {
        typeof(MainWindow).GetField("_isQuitting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Capture(MainWindow window, string output, string name, int width, int height)
    {
        var root = (FrameworkElement)window.Content;
        var size = new System.Windows.Size(width, height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Pump();
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    public static void Run(string output)
    {
        var clock = new Clock();
        var provider = new DemoProvider { Current = new(AppLicenseKind.Trial, clock.Utc.AddDays(15)) };
        using var license = new AppLicenseService(provider, clock);
        var session = new Session(); var starts = 0;
        var controller = new DisplayWallpaperController((_, _) => { starts++; return Task.FromResult<IWallpaperSession>(session); });
        var settings = new AppSettingsStore(Path.Combine(output, "license-settings.json"));
        settings.Save(new AppSettings { SelectedWallpaperId = "living-fire" });
        var window = new MainWindow(settings, Path.Combine(output, "license-library"), controller, license);
        try
        {
            license.RefreshAsync().GetAwaiter().GetResult(); license.RefreshPriceAsync().GetAwaiter().GetResult();
            Capture(window, output, "trial-15-wide", 1280, 780);
            Capture(window, output, "trial-15-compact", 760, 620);
            Assert(((Button)window.FindName("StartButton")).IsEnabled, "Valid trial could not apply a wallpaper.");
            Invoke(window, "StartButton_Click"); Pump();
            Assert(starts == 1 && controller.IsRunning, "Valid trial did not start wallpaper.");
            var before = File.ReadAllText(Path.Combine(output, "license-settings.json"));
            clock.Utc = clock.Utc.AddDays(14);
            license.EvaluateTime();
            Capture(window, output, "trial-1-compact", 760, 620);
            Assert(((TextBlock)window.FindName("LicenseBannerTitle")).Text.StartsWith("1 day"), "Remaining-day label did not update.");
            clock.Utc = clock.Utc.AddDays(1); license.EvaluateTime(); Pump();
            Assert(session.Disposed && !controller.IsRunning, "Trial expiration did not stop desktop playback.");
            Assert(File.ReadAllText(Path.Combine(output, "license-settings.json")) == before, "Expiration changed saved preferences.");
            Assert(((FrameworkElement)window.FindName("LibraryWorkspace")).Visibility == Visibility.Collapsed, "Expired library still visible.");
            Invoke(window, "StartButton_Click"); Pump();
            Assert(starts == 1, "Expired license allowed another desktop start.");
            Capture(window, output, "trial-expired-wide", 1280, 780);
            Capture(window, output, "trial-expired-compact", 640, 520);
            license.PurchaseAsync().GetAwaiter().GetResult(); Pump();
            Assert(license.Snapshot.Kind == AppLicenseKind.Owned && starts == 1, "Confirmed purchase did not unlock or auto-started playback.");
            Assert(((FrameworkElement)window.FindName("LicenseBanner")).Visibility == Visibility.Collapsed, "Paid user still sees trial banner.");
            Capture(window, output, "license-owned", 760, 620);
            Invoke(window, "BackToLibrary_Click");
            Assert(((Button)window.FindName("StartButton")).IsEnabled, "Purchased license did not restore Apply.");
            Console.WriteLine("PASS: trial countdown, adaptive purchase banner, hidden-window expiration stops playback, preferences preserved, expired Apply blocked, confirmed purchase unlocks without autoplay");
        }
        finally { Close(window); }
        var offline = new DemoProvider { Offline = true };
        using var offlineLicense = new AppLicenseService(offline);
        window = new MainWindow(new AppSettingsStore(Path.Combine(output, "offline-settings.json")), Path.Combine(output, "offline-library"), license: offlineLicense);
        try
        {
            offlineLicense.RefreshAsync().GetAwaiter().GetResult();
            Capture(window, output, "license-unavailable", 760, 620);
            Assert(((Button)window.FindName("LicenseGateBuyButton")).Visibility == Visibility.Collapsed, "Unknown license offered purchase before verification.");
            offline.Offline = false; offline.Current = new(AppLicenseKind.Owned);
            Invoke(window, "CheckLicense_Click"); Pump();
            Assert(offlineLicense.CanPlay, "Retry did not recover Store license.");
            Console.WriteLine("PASS: unavailable-license screen and successful Check license recovery");
        }
        finally { Close(window); }
    }
    public static void ShowDemo(System.Windows.Application app, string output)
    {
        var provider = new DemoProvider();
        var license = new AppLicenseService(provider);
        var settings = new AppSettingsStore(Path.Combine(output, "demo-settings.json"));
        settings.Save(new AppSettings { SelectedWallpaperId = "living-fire" });
        // Only commerce is simulated; wallpaper application uses the production desktop pipeline.
        var controller = new DisplayWallpaperController();
        var window = new MainWindow(settings, Path.Combine(output, "demo-library"), controller, license) { Title = "HYPNIX · License preview" };
        var panel = new StackPanel { Margin = new Thickness(20, 12, 20, 12) };
        panel.Children.Add(new TextBlock { Text = "Demonstração local · compras simuladas · wallpaper real por monitor", Margin = new Thickness(0, 0, 0, 10), Foreground = Brushes.White });
        var choices = new ComboBox { ItemsSource = new[] { "15 dias de teste", "Último dia", "Teste terminado", "Licença comprada" }, SelectedIndex = 0 };
        panel.Children.Add(choices);
        var toolbar = new Window { Title = "HYPNIX · Estados da licença (demo)", Width = 610, Height = 140, ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(19, 25, 35)), Content = panel, ShowInTaskbar = true };
        choices.SelectionChanged += (_, _) => {
            Invoke(window, "BackToLibrary_Click");
            provider.Set(choices.SelectedIndex switch { 2 => AppLicenseKind.Expired, 3 => AppLicenseKind.Owned, _ => AppLicenseKind.Trial }, choices.SelectedIndex == 1 ? 1 : 15);
        };
        window.Loaded += (_, _) => {
            toolbar.Owner = window;
            var area = SystemParameters.WorkArea;
            toolbar.Left = Math.Clamp(window.Left + (window.Width - toolbar.Width) / 2, area.Left, Math.Max(area.Left, area.Right - toolbar.Width));
            toolbar.Top = window.Top >= toolbar.Height + 8 ? window.Top - toolbar.Height - 8 : Math.Min(area.Bottom - toolbar.Height, window.Top + window.Height + 8);
            toolbar.Show(); window.Activate();
        };
        toolbar.Closed += (_, _) => { Close(window); app.Shutdown(); };
        app.Run(window);
    }
}
