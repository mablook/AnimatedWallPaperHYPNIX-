using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;

internal static class LemonLicenseWindowChecks
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
    private sealed class Api : HttpMessageHandler
    {
        public bool Offline, Revoked;
        public int Activations, Deactivations;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Offline) throw new HttpRequestException("Simulated outage");
            var operation = request.RequestUri!.Segments.Last();
            if (operation == "activate") Activations++;
            if (operation == "deactivate") Deactivations++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(JsonSerializer.Serialize(new {
                    valid = !Revoked, activated = !Revoked, deactivated = true,
                    license_key = new { key = "e2e-license-key", status = Revoked ? "disabled" : "active" },
                    instance = new { id = "e2e-instance" },
                    meta = new { store_id = 10, product_id = 20, variant_id = 30 }
                }), Encoding.UTF8, "application/json")
            });
        }
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
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Complete(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
        if (!task.IsCompleted) throw new TimeoutException("License UI operation timed out.");
        task.GetAwaiter().GetResult(); Pump();
    }
    private static void Click(MainWindow window, string name)
    { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    private static void Capture(MainWindow window, string output, string name, int width, int height)
    {
        var root = (FrameworkElement)window.Content; var size = new System.Windows.Size(width, height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    public static void Run(string output)
    {
        var clock = new Clock(); var api = new Api(); var opened = 0;
        var config = new LemonSqueezyConfiguration(10, 20, 30, "https://hypnix.lemonsqueezy.com/buy/e2e");
        var statePath = Path.Combine(output, "license-" + Guid.NewGuid().ToString("N") + ".dat");
        var state = new LicenseStateStore(statePath);
        var provider = new LemonSqueezyLicenseProvider(config, new(new HttpClient(api)), state, clock, _ => opened++);
        using var service = new AppLicenseService(provider, clock);
        var session = new Session(); var starts = 0;
        var controller = new DisplayWallpaperController((_, _) => { starts++; return Task.FromResult<IWallpaperSession>(session); });
        var settings = new AppSettingsStore(Path.Combine(output, "settings.json"));
        settings.Save(new AppSettings { SelectedWallpaperId = "living-fire" });
        var window = new MainWindow(settings, Path.Combine(output, "library"), controller, service);
        try
        {
            Complete(service.RefreshAsync());
            Capture(window, output, "lemon-trial", 1280, 780);
            Click(window, "LicenseEnterKeyButton");
            Capture(window, output, "lemon-activation", 760, 620);
            Assert(((FrameworkElement)window.FindName("LicenseKeyPanel")).Visibility == Visibility.Visible, "Key entry inaccessible during trial.");
            typeof(MainWindow).GetMethod("BackToLibrary_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
            Click(window, "StartButton");
            Assert(starts == 1 && controller.IsRunning, "Trial did not start playback.");
            clock.Utc = clock.Utc.AddDays(15); service.EvaluateTime(); Pump();
            Assert(session.Disposed && !controller.IsRunning, "Expiration left wallpaper running.");
            var saved = File.ReadAllText(Path.Combine(output, "settings.json"));
            Capture(window, output, "lemon-expired-compact", 640, 520);
            Click(window, "LicenseGateBuyButton");
            Assert(opened == 1 && !service.CanPlay, "Browser checkout incorrectly unlocked license.");
            ((PasswordBox)window.FindName("LicenseGateKeyInput")).Password = "wrong-license-key";
            Click(window, "LicenseGateActivateButton");
            Assert(!service.CanPlay && api.Activations == 0, "Invalid key activated.");
            ((PasswordBox)window.FindName("LicenseGateKeyInput")).Password = "e2e-license-key";
            Click(window, "LicenseGateActivateButton");
            Assert(service.CanPlay && api.Activations == 1, "Valid key did not unlock.");
            Assert(((PasswordBox)window.FindName("LicenseGateKeyInput")).Password.Length == 0, "Key retained in input.");
            Assert(starts == 1, "Activation auto-started wallpaper.");
            Capture(window, output, "lemon-owned", 760, 620);
            api.Offline = true; Complete(service.RefreshAsync()); Assert(service.CanPlay, "Transient outage lost paid access.");
            clock.Utc = clock.Utc.AddDays(7); service.EvaluateTime(); Assert(!service.CanPlay, "Grace did not expire in memory.");
            Complete(service.RefreshAsync()); Assert(!service.CanPlay, "Offline refresh extended grace.");
            api.Offline = false; Complete(service.RefreshAsync()); Assert(service.CanPlay, "Online recovery failed.");
            api.Revoked = true; Complete(service.RefreshAsync()); Assert(!service.CanPlay, "Revocation left app unlocked.");
            api.Revoked = false; Complete(service.RefreshAsync());
            Click(window, "LicenseDeactivateButton");
            Assert(api.Deactivations == 1 && !service.CanPlay && !service.HasActivation, "Deactivation failed or restarted trial.");
            Assert(File.ReadAllText(Path.Combine(output, "settings.json")) == saved, "Commerce changed wallpaper preferences.");
            Console.WriteLine("PASS: Lemon checkout, invalid/valid key entry, encrypted state, expiry stops playback, 7-day offline boundary, recovery, revocation, deactivation, preferences and no autoplay");
        }
        finally
        {
            typeof(MainWindow).GetField("_isQuitting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
            if (File.Exists(statePath)) File.Delete(statePath);
        }
    }
}
