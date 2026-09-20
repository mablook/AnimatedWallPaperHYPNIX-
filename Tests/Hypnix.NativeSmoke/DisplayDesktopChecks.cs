using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;

// Exercises the real UI -> coordinator -> native renderer -> Explorer path. All preferences,
// libraries and windows belong to this test; existing HYPNIX processes are never stopped.
internal static class DisplayDesktopChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private sealed record TrackedHost(WallpaperRequest Request, IWallpaperSession Session,
        NativeWallpaperHost Host, IntPtr Handle);

    // License behavior is covered separately. This test must never query or purchase a Store license.
    private sealed class DesktopTestLicense : IAppLicenseProvider
    {
        public bool IsStoreManaged => false;
        public event Action? LicenseChanged { add { } remove { } }
        public void Initialize(IntPtr owner) { }
        public Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken token) => Task.FromResult(new AppLicenseSnapshot(AppLicenseKind.Unmanaged));
        public Task<string?> GetPriceAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task<AppPurchaseResult> PurchaseAsync(CancellationToken token) => throw new InvalidOperationException("Purchases are outside this desktop test.");
        public void Dispose() { }
    }

    public static void Run(string output, string? mediaTools = null, string? videoPath = null)
    {
        Directory.CreateDirectory(output);
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); // physical monitor pixels, including mixed DPI
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var started = DateTimeOffset.UtcNow;
        var checks = new List<string>();
        var geometryEvidence = new List<object>();
        var tracked = new List<TrackedHost>();
        var decoders = new List<Process>();
        var videoRequested = !string.IsNullOrWhiteSpace(mediaTools) || !string.IsNullOrWhiteSpace(videoPath);
        var videoStatus = videoRequested ? "Not yet executed" : "Skipped: no media-tools directory and video were provided";
        DesktopWorker.WallpaperTarget[] monitors = [];
        MainWindow? window = null;
        DisplayWallpaperController? controller = null;
        string? failure = null;

        void Passed(string message) { checks.Add(message); Console.WriteLine("PASS: " + message); }
        DisplayWallpaperController CreateController() => new(async (request, token) =>
        {
            var session = await WallpaperSessionFactory.CreateAsync(request, token);
            var host = (NativeWallpaperHost?)session.GetType().GetField("_host", PrivateInstance)?.GetValue(session);
            if (host is null) { session.Dispose(); throw new InvalidOperationException("Real session did not expose a native host."); }
            tracked.Add(new(request, session, host, host.Handle));
            return session;
        });
        TrackedHost ActiveHost(string deviceId)
        {
            var active = controller!.ActiveRequests.GetValueOrDefault(deviceId)
                ?? throw new InvalidOperationException("Display has no active request: " + deviceId);
            return tracked.Last(host => host.Request.Target?.DeviceId == deviceId &&
                host.Request.Id == active.Id && host.Session.IsHealthy && IsWindow(host.Handle));
        }
        void CheckHost(string step, DesktopWorker.WallpaperTarget monitor)
        {
            var host = ActiveHost(monitor.DeviceId);
            uint? presentBefore = null, presentAfter = null;
            string? videoBefore = null, videoAfter = null;
            if (host.Request.Kind == WallpaperKind.ExampleVideo)
            {
                videoBefore = DecodedFrameHash(host);
                WaitUntil(() => !host.Session.IsHealthy || DecodedFrameHash(host) is { Length: > 0 } hash && hash != videoBefore,
                    window!, step + " decoded video frames");
                videoAfter = DecodedFrameHash(host);
            }
            else
            {
                // Start prepares the first GPU frame synchronously. Require several later
                // successful Present calls so a host with a stalled renderer cannot pass.
                presentBefore = PresentCount(host);
                WaitUntil(() => !host.Session.IsHealthy || PresentCount(host) >= presentBefore.Value + 3,
                    window!, step + " GPU frames");
                presentAfter = PresentCount(host);
            }
            Assert(host.Session.IsHealthy && IsWindowVisible(host.Handle), step + ": wallpaper is unhealthy or hidden.");
            var parent = GetParent(host.Handle);
            Assert(parent != IntPtr.Zero, step + ": wallpaper has no desktop parent.");
            var name = new StringBuilder(256);
            GetClassName(parent, name, name.Capacity);
            Assert(name.ToString() is "Progman" or "WorkerW", step + ": wallpaper is not attached to an Explorer desktop window.");
            GetWindowThreadProcessId(parent, out var processId);
            using (var process = Process.GetProcessById((int)processId))
                Assert(process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase), step + ": desktop parent is not Explorer.");
            var hasBounds = GetWindowRect(host.Handle, out var bounds);
            var hasClient = GetClientRect(host.Handle, out var client);
            Assert(hasBounds && hasClient, step + ": cannot read native bounds.");
            var origin = new NativePoint { X = bounds.Left, Y = bounds.Top };
            MapWindowPoints(IntPtr.Zero, parent, ref origin, 1);
            Assert((origin.X, origin.Y, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top) ==
                (monitor.X, monitor.Y, monitor.Width, monitor.Height), step + ": native host does not occupy the requested monitor rectangle.");
            Assert(client.Right - client.Left == monitor.Width && client.Bottom - client.Top == monitor.Height,
                step + ": native client size differs from physical monitor dimensions.");
            var targets = (DesktopWorker.WallpaperTarget[]?)typeof(NativeWallpaperHost).GetField("_renderTargets", PrivateInstance)!.GetValue(host.Host);
            Assert(targets is { Length: 1 } && targets[0].X == 0 && targets[0].Y == 0 &&
                targets[0].Width == monitor.Width && targets[0].Height == monitor.Height,
                step + ": renderer did not use exactly one local viewport.");
            geometryEvidence.Add(new
            {
                Step = step, Wallpaper = host.Request.Id, monitor.DeviceId, monitor.DeviceName,
                Handle = $"0x{host.Handle.ToInt64():X}", Parent = $"0x{parent.ToInt64():X}", ParentClass = name.ToString(),
                ExplorerProcessId = processId, DesktopBounds = new[] { origin.X, origin.Y, monitor.Width, monitor.Height },
                ScreenBounds = new[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
                LocalViewport = new[] { 0, 0, monitor.Width, monitor.Height }, monitor.DpiX, monitor.DpiY,
                PresentCountBefore = presentBefore, PresentCountAfter = presentAfter,
                DecodedFrameHashBefore = videoBefore, DecodedFrameHashAfter = videoAfter,
                DecoderProcessId = host.Session.ProcessId
            });
        }

        try
        {
            monitors = DesktopWorker.GetMonitorTargets();
            Assert(monitors.Length >= 2, "This E2E requires at least two connected physical displays; it was not executed.");
            if (videoRequested)
            {
                Assert(!string.IsNullOrWhiteSpace(mediaTools) && !string.IsNullOrWhiteSpace(videoPath),
                    "The optional video check requires both a media-tools directory and a video path.");
                mediaTools = Path.GetFullPath(mediaTools!);
                videoPath = Path.GetFullPath(videoPath!);
                Assert(File.Exists(Path.Combine(mediaTools, "ffmpeg.exe")) && File.Exists(Path.Combine(mediaTools, "ffprobe.exe")),
                    "The supplied media-tools directory must contain ffmpeg.exe and ffprobe.exe.");
                Assert(File.Exists(videoPath), "The supplied video file does not exist.");
            }
            var store = new AppSettingsStore(Path.Combine(output, "display-desktop-settings.json"));
            Assert(store.Save(new AppSettings
            {
                SelectedWallpaperId = "living-fire", AppPauseMode = 0, AudioReactive = false,
                FramesPerSecond = 15, PreviewPaneCollapsed = true, MediaToolsDirectory = mediaTools,
                Videos = videoRequested ? [new LocalVideo("e2e-monitor-video", videoPath!, "E2E monitor video")] : []
            }), "Could not create isolated desktop-test settings.");
            controller = CreateController();
            window = CreateWindow(store, controller, output);
            window.Show();
            WaitUntil(() => window.IsLoaded && Control<Button>(window, "StartButton").IsEnabled, window, "Initial window readiness");
            window.Hide(); // Rendering is on the real desktop; no extra animated preview consumes GPU during assertions.

            Select(window, monitors[0].DeviceId, "living-fire");
            Click(window, "StartButton");
            WaitUntil(() => IsApplied(controller, monitors[0], "living-fire") && Ready(window), window, "Apply first display");
            CheckHost("first-display", monitors[0]);
            var originalFirst = ActiveHost(monitors[0].DeviceId);
            Select(window, monitors[1].DeviceId, "event-horizon");
            Click(window, "StartButton");
            WaitUntil(() => IsApplied(controller, monitors[1], "event-horizon") && Ready(window), window, "Apply second display");
            Assert(controller.ActiveRequests.Count == 2, "Independent apply did not leave exactly two active displays.");
            CheckHost("two-distinct-wallpapers-first", monitors[0]);
            CheckHost("two-distinct-wallpapers-second", monitors[1]);
            var originalSecond = ActiveHost(monitors[1].DeviceId);
            Assert(originalFirst.Handle != originalSecond.Handle, "Independent displays share the same HWND.");
            Passed("UI applies Living Fire and Event Horizon to separate real Explorer-hosted monitor windows");

            Process? videoDecoder = null;
            if (videoRequested)
            {
                var secondBeforeVideo = PresentCount(originalSecond);
                Select(window, monitors[0].DeviceId, "e2e-monitor-video");
                Click(window, "StartButton");
                WaitUntil(() => IsApplied(controller, monitors[0], "e2e-monitor-video") && Ready(window), window, "Apply video to first display");
                Assert(!IsWindow(originalFirst.Handle), "Applying video leaked the replaced wallpaper host.");
                originalFirst = ActiveHost(monitors[0].DeviceId);
                Assert(originalFirst.Session is VideoWallpaperSession && originalFirst.Session.ProcessId.HasValue,
                    "Video apply did not create a real decoder session.");
                videoDecoder = Process.GetProcessById(originalFirst.Session.ProcessId!.Value);
                _ = videoDecoder.Handle; // retain this process identity even after the decoder exits
                decoders.Add(videoDecoder);
                CheckHost("mixed-video-first", monitors[0]);
                WaitUntil(() => PresentCount(originalSecond) >= secondBeforeVideo + 3, window, "Shader advances alongside video");
                Assert(ActiveHost(monitors[1].DeviceId).Handle == originalSecond.Handle && originalSecond.Session.IsHealthy && !videoDecoder.HasExited,
                    "Starting video restarted the other display or failed to keep its decoder alive.");
                CheckHost("mixed-shader-second", monitors[1]);
                Passed("Video frames change on one monitor while the other monitor's original shader HWND keeps presenting");
            }

            Select(window, monitors[0].DeviceId, "spectral-bloom");
            Click(window, "StartButton");
            WaitUntil(() => IsApplied(controller, monitors[0], "spectral-bloom") && Ready(window), window, "Replace first display");
            Assert(!IsWindow(originalFirst.Handle), "Replacing display one leaked its old wallpaper HWND.");
            Assert(ActiveHost(monitors[1].DeviceId).Handle == originalSecond.Handle && originalSecond.Session.IsHealthy,
                "Replacing display one restarted or damaged display two.");
            CheckHost("replace-first", monitors[0]);
            Passed("Replacing one display destroys only its previous HWND and preserves the other display");
            if (videoDecoder is not null)
            {
                WaitUntil(() => videoDecoder.HasExited, window, "Replaced video decoder cleanup");
                videoStatus = "Passed: mixed video/shader, changing decoded frames, target geometry and decoder exit";
                Passed("Replacing the video terminates its FFmpeg decoder and destroys its HWND without interrupting the other monitor");
            }

            // This assertion tests the coordinator/native boundary. Stop coverage polling in
            // this test window so unrelated user window changes cannot overwrite the injected policy.
            ((ForegroundAppMonitor)typeof(MainWindow).GetField("_foregroundMonitor", PrivateInstance)!.GetValue(window)!).Dispose();
            controller.SetPausedDisplays([monitors[0].DeviceId]);
            var pausedFirst = ActiveHost(monitors[0].DeviceId);
            Assert(controller.IsDisplayPaused(monitors[0].DeviceId) && IsNativePaused(pausedFirst) &&
                !controller.IsDisplayPaused(monitors[1].DeviceId) && !IsNativePaused(originalSecond),
                "Selective pause did not reach only the requested native render worker.");
            PumpFor(TimeSpan.FromMilliseconds(300)); // permit any frame already in flight to finish
            var pausedCount = PresentCount(pausedFirst);
            var runningCount = PresentCount(originalSecond);
            PumpFor(TimeSpan.FromMilliseconds(400));
            Assert(PresentCount(pausedFirst) == pausedCount && PresentCount(originalSecond) > runningCount,
                "Paused display kept presenting or the other display stopped presenting.");
            controller.SetPausedDisplays([]);
            Assert(!IsNativePaused(pausedFirst) && !IsNativePaused(originalSecond), "Selective resume did not resume native workers.");
            WaitUntil(() => PresentCount(pausedFirst) > pausedCount, window, "Resume first display GPU");
            Passed("Selective pause freezes GPU presentations on one display while the other advances; resume restarts presentations");

            Click(window, "StopDisplayButton");
            WaitUntil(() => !controller.ActiveRequests.ContainsKey(monitors[0].DeviceId) && Ready(window), window, "Stop first display");
            Assert(!IsWindow(pausedFirst.Handle) && ActiveHost(monitors[1].DeviceId).Handle == originalSecond.Handle,
                "Stop display removed the wrong host or left the stopped host alive.");
            Passed("Stop display removes only the selected wallpaper");

            Select(window, monitors[0].DeviceId, "neon-ribbons");
            Click(window, "ApplyAllButton");
            WaitUntil(() => controller.ActiveRequests.Count == monitors.Length &&
                monitors.All(monitor => IsApplied(controller, monitor, "neon-ribbons")) && Ready(window), window, "Apply to all displays");
            foreach (var monitor in monitors) CheckHost("apply-all", monitor);
            Assert(monitors.Select(monitor => ActiveHost(monitor.DeviceId).Handle).Distinct().Count() == monitors.Length,
                "Apply all did not retain independent native hosts.");
            Assert(!IsWindow(originalSecond.Handle), "Apply all leaked the replaced second display HWND.");
            Passed("Apply all assigns the selected wallpaper to every connected display using independent hosts");

            Invoke(window, "SavePreferences");
            var saved = store.Load();
            Assert(monitors.All(monitor => saved.DisplayWallpapers.TryGetValue(monitor.DeviceId, out var value) &&
                value.Enabled && value.WallpaperId == "neon-ribbons"), "Assignments were not saved by stable display identity.");
            var beforeClose = tracked.Select(host => host.Handle).ToArray();
            Close(window); window = null;
            Assert(beforeClose.All(handle => !IsWindow(handle)), "Closing the first window leaked a desktop host.");
            controller = CreateController();
            window = CreateWindow(store, controller, output);
            window.Show();
            WaitUntil(() => window.IsLoaded && controller.ActiveRequests.Count == monitors.Length &&
                monitors.All(monitor => IsApplied(controller, monitor, "neon-ribbons")) && Ready(window), window, "Restore saved assignments on startup");
            window.Hide();
            foreach (var monitor in monitors) CheckHost("startup-restore", monitor);
            Passed("Closing releases all hosts; a fresh MainWindow restores persisted assignments by monitor identity");

            Click(window, "StopButton");
            WaitUntil(() => !controller.IsRunning && Ready(window), window, "Stop all displays");
            Invoke(window, "SavePreferences");
            Assert(tracked.All(host => !IsWindow(host.Handle)), "Stop all leaked a real wallpaper HWND.");
            Assert(store.Load().DisplayWallpapers.Values.All(value => !value.Enabled), "Stop all did not persist disabled assignments.");
            Passed("Stop all destroys every test wallpaper HWND and persists stopped assignments");
            Close(window); window = null;
        }
        catch (Exception exception) { failure = exception.ToString(); throw; }
        finally
        {
            if (window is not null) Close(window);
            controller?.Dispose();
            foreach (var host in tracked) host.Session.Dispose();
            foreach (var decoder in decoders) decoder.WaitForExit(3000);
            var remaining = tracked.Where(host => IsWindow(host.Handle)).Select(host => $"0x{host.Handle.ToInt64():X}").ToArray();
            var remainingDecoders = decoders.Where(decoder => !decoder.HasExited).Select(decoder => decoder.Id).ToArray();
            File.WriteAllText(Path.Combine(output, "display-desktop-report.json"), JsonSerializer.Serialize(new
            {
                StartedUtc = started, FinishedUtc = DateTimeOffset.UtcNow, Passed = failure is null && remaining.Length == 0 && remainingDecoders.Length == 0,
                Failure = failure, MonitorCount = monitors.Length, Checks = checks, GeometryEvidence = geometryEvidence,
                RemainingTestWindows = remaining, RemainingDecoderProcesses = remainingDecoders,
                VideoSkipped = !videoRequested, VideoCheckStatus = videoStatus, SettingsScope = "Isolated under this report directory",
                PauseScope = "Coordinator to native worker; foreign-app coverage detection is covered separately",
                DesktopScreenshotsCaptured = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var decoder in decoders) decoder.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    private static MainWindow CreateWindow(AppSettingsStore store, DisplayWallpaperController controller, string output)
        => new(store, Path.Combine(output, "display-desktop-library"), controller,
            new AppLicenseService(new DesktopTestLicense())) { Title = "HYPNIX · Display desktop E2E" };
    private static T Control<T>(MainWindow window, string name) where T : FrameworkElement
        => window.FindName(name) as T ?? throw new InvalidOperationException("Missing UI control: " + name);
    private static bool Ready(MainWindow window) => Control<Button>(window, "StartButton").IsEnabled;
    private static bool IsApplied(DisplayWallpaperController controller, DesktopWorker.WallpaperTarget monitor, string wallpaper)
        => controller.ActiveRequests.TryGetValue(monitor.DeviceId, out var request) && request.Id == wallpaper && controller.IsHealthy;
    private static bool IsNativePaused(TrackedHost host)
        => (bool)typeof(NativeWallpaperHost).GetField("_paused", PrivateInstance)!.GetValue(host.Host)!;
    private static string? DecodedFrameHash(TrackedHost host)
    {
        var sync = typeof(NativeWallpaperHost).GetField("_videoFrameLock", PrivateInstance)!.GetValue(host.Host)!;
        lock (sync)
        {
            var bytes = (byte[]?)typeof(NativeWallpaperHost).GetField("_videoFrame", PrivateInstance)!.GetValue(host.Host);
            return bytes is { Length: > 0 } ? Convert.ToHexString(SHA256.HashData(bytes)) : null;
        }
    }
    private static uint PresentCount(TrackedHost host)
    {
        var renderer = typeof(NativeWallpaperHost).GetField("_aethelisGpuRenderer", PrivateInstance)!.GetValue(host.Host);
        var swapField = "_swapChain";
        if (renderer is null)
        {
            renderer = typeof(NativeWallpaperHost).GetField("_fireGpuRenderer", PrivateInstance)!.GetValue(host.Host);
            swapField = "swap";
        }
        Assert(renderer is not null, "Expected a real initialized GPU renderer.");
        var swap = (Vortice.DXGI.IDXGISwapChain1)renderer!.GetType().GetField(swapField, PrivateInstance)!.GetValue(renderer)!;
        return swap.LastPresentCount;
    }
    private static void Select(MainWindow window, string deviceId, string wallpaper)
    {
        var displays = Control<ComboBox>(window, "TargetDisplayCombo");
        displays.SelectedItem = displays.Items.Cast<object>().Single(item =>
            ((DesktopWorker.WallpaperTarget)item.GetType().GetProperty("Target")!.GetValue(item)!).DeviceId == deviceId);
        var gallery = Control<ListBox>(window, "WallpaperGallery");
        gallery.SelectedItem = gallery.Items.Cast<WallpaperEntry>().Single(item => item.Id == wallpaper);
    }
    private static void Click(MainWindow window, string name)
    {
        var button = Control<Button>(window, name);
        Assert(button.IsEnabled, "UI action is disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }
    private static void Invoke(MainWindow window, string method)
        => (typeof(MainWindow).GetMethod(method, PrivateInstance) ?? throw new MissingMethodException(method)).Invoke(window, null);
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
            if (timeout.Elapsed > TimeSpan.FromSeconds(45))
                throw new TimeoutException(action + " timed out. " + Control<TextBlock>(window, "ErrorText").Text);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref NativePoint point, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr handle);
}
