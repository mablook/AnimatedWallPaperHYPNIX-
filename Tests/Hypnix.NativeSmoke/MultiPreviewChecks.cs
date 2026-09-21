using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Controls;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using WallpaperTarget = AnimatedWallPaper.Services.DesktopWorker.WallpaperTarget;

// Desktop sessions are recorded fakes; every visible preview uses the production GPU
// renderer. This verifies UI and rendering without changing the user's real desktop.
internal static class MultiPreviewChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly WallpaperTarget[] Displays =
    [
        new(-2560, 0, 2560, 1440, "MONITOR:preview-left", "DISPLAY1", 144, 144),
        new(0, -180, 1920, 1080, "MONITOR:preview-right", "DISPLAY2", 96, 96)
    ];
    private sealed record LiveHost(string DisplayId, WallpaperPreviewControl Preview,
        WallpaperController Controller, IWallpaperSession Session, NativeWallpaperHost Host, IntPtr Handle);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var started = DateTimeOffset.UtcNow;
        var checks = new List<string>();
        var frameEvidence = new List<object>();
        var fireEvidence = new List<object>();
        var tracked = new HashSet<IntPtr>();
        MainWindow? window = null;
        string? failure = null;
        var starts = 0;
        try
        {
            var store = new AppSettingsStore(Path.Combine(output, "multi-preview-settings.json"));
            Assert(store.Save(new AppSettings
            {
                SelectedWallpaperId = "living-fire", AppPauseMode = 0, AudioReactive = false,
                FramesPerSecond = 15, PreviewPaneCollapsed = true
            }), "Could not initialize isolated preview settings.");
            var controller = new DisplayWallpaperController((_, _) =>
            {
                starts++;
                return Task.FromResult<IWallpaperSession>(new FakeSession());
            });
            window = new MainWindow(store, Path.Combine(output, "multi-preview-library"), controller,
                new AppLicenseService(new UnmanagedProvider()), () => Displays)
                { Title = "HYPNIX · Multiple preview E2E", Width = 1280, Height = 780 };
            window.Show();
            WaitUntil(() => window.IsLoaded && Control<Button>(window, "StartButton").IsEnabled,
                window, "Initial window readiness");
            Select(window, 0, "living-fire");
            Click(window, "StartButton");
            WaitUntil(() => controller.ActiveRequests.GetValueOrDefault(Displays[0].DeviceId)?.Id == "living-fire"
                && Control<Button>(window, "StartButton").IsEnabled, window, "First desktop assignment");
            Select(window, 1, "kaleidoscope");
            Click(window, "StartButton");
            WaitUntil(() => controller.ActiveRequests.GetValueOrDefault(Displays[1].DeviceId)?.Id == "kaleidoscope"
                && Control<Button>(window, "StartButton").IsEnabled, window, "Second desktop assignment");
            Click(window, "PreviewButton");
            Mode(window, 1);
            var actual = WaitForPreviews(window, 2);
            RecordFrames("two-connected-displays", actual);
            Capture(window, output, "two-connected-displays");
            Assert(actual.Select(host => host.Controller.ActiveRequest!.Id).ToHashSet()
                .SetEquals(["living-fire", "kaleidoscope"]), "Connected displays did not show their independent assignments.");
            var firstBefore = actual.Single(host => host.DisplayId == Displays[0].DeviceId);
            var secondBefore = actual.Single(host => host.DisplayId == Displays[1].DeviceId);
            Assert(firstBefore.Handle != secondBefore.Handle, "Connected previews share a native HWND.");

            Select(window, 0, "event-horizon");
            Click(window, "StartButton");
            WaitUntil(() => controller.ActiveRequests.GetValueOrDefault(Displays[0].DeviceId)?.Id == "event-horizon"
                && Control<Button>(window, "StartButton").IsEnabled, window, "Independent replacement");
            actual = WaitForPreviews(window, 2);
            WaitUntil(() => actual.Single(host => host.DisplayId == Displays[0].DeviceId).Controller.ActiveRequest?.Id == "event-horizon",
                window, "Changed display preview");
            actual = WaitForPreviews(window, 2);
            Assert(actual.Single(host => host.DisplayId == Displays[1].DeviceId).Handle == secondBefore.Handle,
                "Changing one display unnecessarily recreated the other preview.");
            Assert(!IsWindow(firstBefore.Handle), "Replacing one preview leaked its old native host.");
            RecordFrames("independent-preview-replacement", actual);
            Passed("Two connected monitor previews animate independently; changing one preserves the other's native HWND");

            var startsBeforeDraft = starts;
            var untouchedRight = actual.Single(host => host.DisplayId == Displays[1].DeviceId).Handle;
            CardChoice(window, Displays[0].DeviceId).SelectedValue = "neon-ribbons";
            actual = WaitForPreviews(window, 2);
            Assert(actual.Single(host => host.DisplayId == Displays[0].DeviceId).Controller.ActiveRequest?.Id == "neon-ribbons",
                "Connected monitor card selection did not update its draft preview.");
            Assert(actual.Single(host => host.DisplayId == Displays[1].DeviceId).Handle == untouchedRight,
                "Choosing a draft restarted the other monitor preview.");
            Assert(starts == startsBeforeDraft && controller.ActiveRequests[Displays[0].DeviceId].Id == "event-horizon"
                && controller.ActiveRequests[Displays[1].DeviceId].Id == "kaleidoscope",
                "Choosing a monitor preview wallpaper applied it without the Apply action.");
            CardChoice(window, Displays[1].DeviceId).SelectedValue = "lotus";
            actual = WaitForPreviews(window, 2);
            Assert(actual.Single(host => host.DisplayId == Displays[0].DeviceId).Controller.ActiveRequest?.Id == "neon-ribbons"
                && actual.Single(host => host.DisplayId == Displays[1].DeviceId).Controller.ActiveRequest?.Id == "lotus",
                "Switching preview cards lost another monitor's uncommitted wallpaper choice.");
            Assert(starts == startsBeforeDraft, "Choosing a second monitor draft applied a desktop wallpaper.");
            RecordFrames("independent-unapplied-drafts", actual);
            Capture(window, output, "two-connected-display-drafts");
            Click(window, "ApplyAllButton");
            WaitUntil(() => controller.ActiveRequests.Count == 2 && controller.ActiveRequests.Values.All(request => request.Id == "lotus")
                && Control<Button>(window, "StartButton").IsEnabled, window, "Apply all updates assignments");
            actual = WaitForPreviews(window, 2);
            Assert(actual.All(host => host.Controller.ActiveRequest?.Id == "lotus"),
                "Apply all left another monitor showing a stale preview draft.");
            RecordFrames("apply-all-refreshes-every-preview", actual);
            Passed("Per-card wallpaper choices remain unapplied drafts across monitor selection; Apply to all refreshes both assignments and previews");

            Invoke(window, "SavePreferences");
            var settingsBefore = File.ReadAllText(storePath());
            var requestsBefore = controller.ActiveRequests.ToDictionary(pair => pair.Key, pair => pair.Value);
            var startsBefore = starts;
            foreach (var count in new[] { 3, 4 })
            {
                Mode(window, count == 3 ? 2 : 3);
                WaitUntil(() => Descendants<ComboBox>(Control<FrameworkElement>(window, "MultiPreview"))
                    .Count(combo => Equals(combo.Tag, "WallpaperSelection")) == count, window, $"{count} simulation selectors");
                var choices = Descendants<ComboBox>(Control<FrameworkElement>(window, "MultiPreview"))
                    .Where(combo => Equals(combo.Tag, "WallpaperSelection")).ToArray();
                var wallpaperIds = new[] { "living-fire", "kaleidoscope", "event-horizon", "neon-ribbons" };
                for (var index = 0; index < count; index++) choices[index].SelectedValue = wallpaperIds[index];
                var simulated = WaitForPreviews(window, count);
                RecordFrames($"simulation-{count}", simulated);
                Assert(simulated.All(host => host.DisplayId.StartsWith("preview-sim-", StringComparison.Ordinal)),
                    "Simulation used a physical monitor identity.");
                Assert(simulated.All(host => host.Controller.ActiveRequest is { Preview: not null, Target: null }),
                    "A simulated render request could target the desktop.");
                Assert(simulated.Select(host => host.Controller.ActiveRequest!.Id).Distinct().Count() == count,
                    "Simulation wallpaper choices were not independent.");
                Assert(!Control<Button>(window, "StartButton").IsEnabled && !Control<Button>(window, "ApplyAllButton").IsEnabled
                    && !Control<Button>(window, "StopDisplayButton").IsEnabled,
                    "Simulation left a desktop mutation action enabled.");
                Assert(!Control<ComboBox>(window, "TargetDisplayCombo").IsEnabled
                    && !Control<ComboBox>(window, "PreviewDisplayCombo").IsEnabled,
                    "Simulation left a physical display selector enabled.");
                foreach (var handler in new[] { "StartButton_Click", "ApplyAll_Click", "StopDisplay_Click", "VisualizerSettingsButton_Click" })
                    Invoke(window, handler, window, new RoutedEventArgs());
                PumpFor(TimeSpan.FromMilliseconds(500));
                Invoke(window, "SavePreferences");
                Assert(starts == startsBefore && requestsBefore.Count == controller.ActiveRequests.Count
                    && requestsBefore.All(pair => controller.ActiveRequests.GetValueOrDefault(pair.Key) == pair.Value),
                    "Simulation or a guarded action changed a real desktop assignment.");
                Assert(File.ReadAllText(storePath()) == settingsBefore, "Simulation modified saved desktop settings.");
                Assert(typeof(MainWindow).GetField("_settingsWindow", PrivateInstance)!.GetValue(window) is null,
                    "Simulation opened the real display customization window.");

                foreach (var size in new[] { new System.Windows.Size(1280, 780), new System.Windows.Size(760, 620), new System.Windows.Size(640, 520) })
                {
                    window.Width = size.Width; window.Height = size.Height;
                    PumpFor(TimeSpan.FromMilliseconds(350));
                    simulated = WaitForPreviews(window, count);
                    AssertLayout(window, simulated, size);
                    RecordFrames($"simulation-{count}-{size.Width}", simulated);
                    Capture(window, output, $"simulation-{count}-{size.Width}");
                }
                Passed($"{count} simulated monitors animate with independent wallpaper choices, fit 1280/760/640 widths, and cannot change desktop assignments or preferences");
                window.Width = 1280; window.Height = 780;
                PumpFor(TimeSpan.FromMilliseconds(350));
            }

            // Exercise the user's exact route: real MainWindow -> simulated card ->
            // Living Fire -> monitor format changes, with the production GPU host.
            CardChoice(window, "preview-sim-4").SelectedValue = "living-fire";
            var format = CardFormat(window, "preview-sim-4");
            foreach (var size in new[] { new System.Windows.Size(1280, 780), new System.Windows.Size(760, 620), new System.Windows.Size(640, 520) })
            {
                window.Width = size.Width; window.Height = size.Height;
                foreach (var aspect in new[] { 16d / 9, 21d / 9, 32d / 9 })
                {
                    format.SelectedValue = aspect;
                    PumpFor(TimeSpan.FromMilliseconds(350));
                    var hosts = WaitForPreviews(window, 4);
                    var fire = hosts.Single(host => host.DisplayId == "preview-sim-4");
                    AssertLayout(window, hosts, size);
                    foreach (var scale in new[] { 1f, .5f })
                    {
                        var settings = fire.Controller.ActiveRequest!.Settings! with { Scale = scale };
                        fire.Preview.UpdateSettings(settings);
                        var target = fire.Controller.ActiveRequest!.Preview!;
                        var renderer = (FireGpuRenderer)typeof(NativeWallpaperHost).GetField("_fireGpuRenderer", PrivateInstance)!.GetValue(fire.Host)!;
                        // Never create a surface through this inspection: native rendering must
                        // already have created exactly the surface matching the WPF preview.
                        var surfaces = (System.Collections.IDictionary)typeof(FireGpuRenderer).GetField("surfaces", PrivateInstance)!.GetValue(renderer)!;
                        Assert(surfaces.Contains((0, 0, target.Width, target.Height)), "Fire is rendering a different viewport from the preview.");
                        var simulation = renderer.GetSimulation(0, 0, target.Width, target.Height);
                        int expected = FireEmitterLayout.CountForViewport(target.Width, target.Height, scale);
                        WaitUntil(() => simulation.FlameCount == expected, window, "Ultrawide preview flame count");
                        double time = simulation.Time;
                        RecordFrames($"fire-format-{aspect:F3}-window-{size.Width}-scale-{scale}", hosts);
                        WaitUntil(() => simulation.Time > time, window, "Ultrawide fire stays animated");
                        Assert(GetClientRect(fire.Handle, out var rect) && rect.Right == target.Width && rect.Bottom == target.Height,
                            "Native fire window does not fill the simulated monitor.");
                        fireEvidence.Add(new { WindowWidth = size.Width, Aspect = aspect, Scale = scale,
                            target.Width, target.Height, Flames = simulation.FlameCount, Before = time, After = simulation.Time });
                    }
                }
                Capture(window, output, $"fire-super-ultrawide-{size.Width}");
            }
            Invoke(window, "SavePreferences");
            Assert(File.ReadAllText(storePath()) == settingsBefore && starts == startsBefore,
                "Changing simulated formats modified real desktop preferences or sessions.");
            Mode(window, 2); Mode(window, 3);
            Assert(Equals(CardFormat(window, "preview-sim-4").SelectedValue, 32d / 9), "Switching simulation counts reset the ultrawide format.");
            Passed("Living Fire adds sources in actual 16:9/21:9/32:9 simulated previews at three window sizes, including small flames; native dimensions match and animation continues");

            var beforeHide = WaitForPreviews(window, 4);
            window.Hide();
            WaitUntil(() => beforeHide.All(host => !host.Controller.IsRunning && !IsWindow(host.Handle)), window,
                "Hidden preview native cleanup");
            Assert(controller.ActiveRequests.Count == 2 && starts == startsBefore, "Hiding previews changed desktop sessions.");
            window.Show();
            RecordFrames("simulation-restored-after-show", WaitForPreviews(window, 4));
            Mode(window, 1);
            RecordFrames("connected-restored-after-simulation", WaitForPreviews(window, 2));
            Mode(window, 0);
            WaitUntil(() => Descendants<WallpaperPreviewControl>(Control<FrameworkElement>(window, "MultiPreview"))
                .All(preview => !PreviewController(preview).IsRunning), window, "Leaving multiple-preview mode cleanup");
            Passed("Hidden previews release their GPU hosts, reopen dynamically, and returning to connected/selected modes preserves desktop assignments");
            Close(window);
            window = null;
            PumpFor(TimeSpan.FromMilliseconds(300));
            Assert(tracked.All(handle => !IsWindow(handle)), "Closing the preview window leaked a native preview HWND.");
            Passed("Closing the window disposes every tracked native preview host");

            string storePath() => Path.Combine(output, "multi-preview-settings.json");
        }
        catch (Exception exception) { failure = exception.ToString(); throw; }
        finally
        {
            if (window is not null) Close(window);
            File.WriteAllText(Path.Combine(output, "multi-preview-report.json"), JsonSerializer.Serialize(new
            {
                Passed = failure is null, Started = started, Completed = DateTimeOffset.UtcNow,
                DesktopSessionsAreTestDoubles = true, PreviewRenderersAreProductionGpu = true,
                DesktopStartCount = starts, Checks = checks, Frames = frameEvidence, FirePreviews = fireEvidence, Failure = failure,
                RemainingTrackedNativeWindows = tracked.Where(IsWindow).Select(handle => $"0x{handle:X}").ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        void Passed(string message) { checks.Add(message); Console.WriteLine("PASS: " + message); }
        void RecordFrames(string step, LiveHost[] hosts)
        {
            foreach (var host in hosts) tracked.Add(host.Handle);
            var counts = hosts.Select(host => PresentCount(host.Host)).ToArray();
            WaitUntil(() => hosts.Select((host, index) => host.Session.IsHealthy && PresentCount(host.Host) >= counts[index] + 3).All(value => value),
                window!, step + " simultaneous GPU presentation");
            foreach (var (host, index) in hosts.Select((host, index) => (host, index)))
            {
                Assert(IsWindowVisible(host.Handle), step + ": native preview is hidden.");
                var parent = GetParent(host.Handle);
                var name = new StringBuilder(256);
                GetClassName(parent, name, name.Capacity);
                Assert(parent != IntPtr.Zero && parent == host.Controller.ActiveRequest!.Preview?.Parent
                    && name.ToString().Equals("STATIC", StringComparison.OrdinalIgnoreCase),
                    step + ": preview is not attached to its WPF preview container: " + name);
                frameEvidence.Add(new { Step = step, host.DisplayId, Wallpaper = host.Controller.ActiveRequest!.Id,
                    Handle = $"0x{host.Handle:X}", ParentClass = name.ToString(), Before = counts[index], After = PresentCount(host.Host) });
            }
        }
    }

    private static LiveHost[] WaitForPreviews(MainWindow window, int count)
    {
        var grid = Control<FrameworkElement>(window, "MultiPreview");
        LiveHost[] hosts = [];
        WaitUntil(() =>
        {
            var previews = Descendants<WallpaperPreviewControl>(grid).ToArray();
            if (previews.Length != count || previews.Any(preview => !preview.IsVisible || !PreviewController(preview).IsHealthy)) return false;
            if (previews.Any(preview =>
            {
                var dpi = VisualTreeHelper.GetDpi(preview);
                var target = PreviewController(preview).ActiveRequest?.Preview;
                return target is null || target.Width != Math.Max(2, (int)(preview.ActualWidth * dpi.DpiScaleX))
                    || target.Height != Math.Max(2, (int)(preview.ActualHeight * dpi.DpiScaleY));
            })) return false;
            hosts = previews.Select(preview =>
            {
                var controller = PreviewController(preview);
                var session = (IWallpaperSession)typeof(WallpaperController).GetField("_session", PrivateInstance)!.GetValue(controller)!;
                var host = (NativeWallpaperHost)session.GetType().GetField("_host", PrivateInstance)!.GetValue(session)!;
                return new LiveHost((string)preview.Tag, preview, controller, session, host, host.Handle);
            }).ToArray();
            return true;
        }, window, $"{count} live previews");
        return hosts;
    }

    private static void AssertLayout(MainWindow window, LiveHost[] hosts, System.Windows.Size size)
    {
        var root = (FrameworkElement)window.Content;
        var grid = Control<FrameworkElement>(window, "MultiPreview");
        var ids = hosts.Select(host => host.DisplayId).ToHashSet();
        var cards = Descendants<Border>(grid).Where(border => border.Tag is string id && ids.Contains(id)).ToArray();
        Assert(cards.Length == hosts.Length, "Preview cards lack stable identities for layout verification.");
        var bounds = cards.Select(card => card.TransformToAncestor(root).TransformBounds(new Rect(card.RenderSize))).ToArray();
        foreach (var (card, index) in cards.Select((card, index) => (card, index)))
        {
            Assert(bounds[index].Left >= 0 && bounds[index].Top >= 0 && bounds[index].Right <= root.ActualWidth + 1
                && bounds[index].Bottom <= root.ActualHeight + 1, $"Preview card {card.Tag} escaped {size}: {bounds[index]}.");
            var preview = hosts.Single(host => host.DisplayId == (string)card.Tag).Preview;
            Assert(preview.ActualWidth >= 30 && preview.ActualHeight >= 20, $"Preview {card.Tag} became unusably small at {size}.");
            var expectedAspect = (double)CardFormat(window, (string)card.Tag).SelectedValue;
            var monitor = Descendants<AspectRatioDecorator>(card).Single();
            Assert(monitor.FrameBrush is not null, "Simulated monitor has no visible screen boundary.");
            Assert(Math.Abs(preview.ActualWidth / preview.ActualHeight - expectedAspect) < 0.03,
                $"Preview {card.Tag} stretched its monitor aspect ratio at {size}.");
            foreach (var header in Descendants<FrameworkElement>(card).Where(element => element.IsVisible && (element is ComboBox || element is Button)))
            {
                var local = header.TransformToAncestor(card).TransformBounds(new Rect(header.RenderSize));
                Assert(local.Left >= -1 && local.Top >= -1 && local.Right <= card.ActualWidth + 1 && local.Bottom <= card.ActualHeight + 1,
                    $"Card {card.Tag} clips {header.GetType().Name} at {size}.");
            }
            for (var other = index + 1; other < bounds.Length; other++)
                Assert(!bounds[index].IntersectsWith(bounds[other]), $"Preview cards overlap at {size}.");
        }
    }

    private static void Capture(MainWindow window, string output, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); // Include the root's outside margins; HWND-backed regions intentionally are not captured.
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    private static WallpaperController PreviewController(WallpaperPreviewControl preview)
        => (WallpaperController)typeof(WallpaperPreviewControl).GetField("_controller", PrivateInstance)!.GetValue(preview)!;
    private static uint PresentCount(NativeWallpaperHost host)
    {
        var renderer = typeof(NativeWallpaperHost).GetField("_aethelisGpuRenderer", PrivateInstance)!.GetValue(host);
        var field = "_swapChain";
        if (renderer is null) { renderer = typeof(NativeWallpaperHost).GetField("_fireGpuRenderer", PrivateInstance)!.GetValue(host); field = "swap"; }
        Assert(renderer is not null, "Expected an initialized production GPU preview renderer.");
        return ((Vortice.DXGI.IDXGISwapChain1)renderer!.GetType().GetField(field, PrivateInstance)!.GetValue(renderer)!).LastPresentCount;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Select(MainWindow window, int displayIndex, string wallpaper)
    {
        Control<ComboBox>(window, "TargetDisplayCombo").SelectedIndex = displayIndex;
        var gallery = Control<ListBox>(window, "WallpaperGallery");
        gallery.SelectedItem = gallery.Items.Cast<WallpaperEntry>().Single(entry => entry.Id == wallpaper);
    }
    private static ComboBox CardChoice(MainWindow window, string displayId)
    {
        var card = Descendants<Border>(Control<FrameworkElement>(window, "MultiPreview"))
            .Single(border => Equals(border.Tag, displayId));
        return Descendants<ComboBox>(card).Single(combo => Equals(combo.Tag, "WallpaperSelection"));
    }
    private static ComboBox CardFormat(MainWindow window, string displayId)
    {
        var card = Descendants<Border>(Control<FrameworkElement>(window, "MultiPreview"))
            .Single(border => Equals(border.Tag, displayId));
        return Descendants<ComboBox>(card).Single(combo => Equals(combo.Tag, "MonitorFormat"));
    }
    private static void Mode(MainWindow window, int index) => Control<ComboBox>(window, "PreviewModeCombo").SelectedIndex = index;
    private static T Control<T>(MainWindow window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidOperationException("Missing control: " + name);
    private static void Click(MainWindow window, string name)
    {
        var button = Control<Button>(window, name);
        Assert(button.IsEnabled, "Action unexpectedly disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static object? Invoke(MainWindow window, string method, params object?[] args)
        => (typeof(MainWindow).GetMethod(method, PrivateInstance) ?? throw new MissingMethodException(method)).Invoke(window, args);
    private static void Close(MainWindow window)
    {
        typeof(MainWindow).GetField("_isQuitting", PrivateInstance)!.SetValue(window, true);
        window.Close();
    }
    private static void WaitUntil(Func<bool> condition, MainWindow window, string action)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException(action + ": " + Control<TextBlock>(window, "ErrorText").Text);
            PumpFor(TimeSpan.FromMilliseconds(25));
        }
    }
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FakeSession : IWallpaperSession
    {
        private bool _disposed;
        public int? ProcessId => null;
        public bool IsHealthy => !_disposed;
        public void Show() { }
        public void SetFrameCap(int framesPerSecond) { }
        public void Resume() { }
        public void Pause() { }
        public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
        public void SetAudioEnabled(bool enabled) { }
        public void Dispose() => _disposed = true;
    }
    private sealed class UnmanagedProvider : IAppLicenseProvider
    {
        public bool IsStoreManaged => false;
        public event Action? LicenseChanged { add { } remove { } }
        public void Initialize(IntPtr owner) { }
        public Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken token) => Task.FromResult(new AppLicenseSnapshot(AppLicenseKind.Unmanaged));
        public Task<string?> GetPriceAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task<AppPurchaseResult> PurchaseAsync(CancellationToken token) => throw new InvalidOperationException("Preview tests do not perform Store purchases.");
        public void Dispose() { }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);
}
