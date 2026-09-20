using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using WallpaperTarget = AnimatedWallPaper.Services.DesktopWorker.WallpaperTarget;

internal static class DisplayWindowChecks
{
    private const string LeftId = "MONITOR:integration-left";
    private const string RightId = "MONITOR:integration-right";
    private static readonly WallpaperTarget Left = new(-2560, 0, 2560, 1440, LeftId, "DISPLAY1", 144, 144);
    private static readonly WallpaperTarget Right = new(0, -180, 1080, 1920, RightId, "DISPLAY2", 96, 96);

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try { RunCore(output); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void RunCore(string output)
    {
        var path = Path.Combine(output, "display-window-settings.json");
        var store = new AppSettingsStore(path);
        Assert(store.Save(new AppSettings { AppPauseMode = 0, AudioReactive = false }), "Could not initialize isolated display settings.");
        WallpaperTarget[] connected = [Left, Right];
        var factory = new SessionFactory();
        var controller = new DisplayWallpaperController(factory.CreateAsync);
        var window = CreateWindow(controller);
        try
        {
            var target = Element<ComboBox>(window, "TargetDisplayCombo");
            Assert(target.Items.Count == 2, "Synthetic monitors did not reach the always-visible selector.");
            SelectDisplay(window, 0);
            SelectWallpaper(window, "living-fire");
            Click(window, "StartButton");
            WaitFor(() => controller.ActiveRequests.Count == 1 && HasWallpaper(controller, LeftId, "living-fire"), window, "Apply to first display");
            Assert(controller.ActiveRequests[LeftId].Target == Left, "First display lost its mixed-DPI desktop coordinates.");
            Assert(!controller.ActiveRequests.ContainsKey(RightId), "Apply to one display also started the other display.");

            SelectDisplay(window, 1);
            SelectWallpaper(window, "event-horizon");
            Click(window, "StartButton");
            WaitFor(() => controller.ActiveRequests.Count == 2 && HasWallpaper(controller, RightId, "event-horizon"), window, "Apply to second display");
            Assert(HasWallpaper(controller, LeftId, "living-fire"), "Applying to display two replaced display one.");
            SelectDisplay(window, 0);
            Assert(SelectedWallpaper(window).Id == "living-fire", "Selecting a display did not restore its assigned wallpaper card.");
            Assert(window.ActiveWallpaperId == "living-fire", "Active badge is not scoped to the selected display.");
            var starts = factory.Created.Count;
            SelectWallpaper(window, "event-horizon");
            Pump();
            Assert(factory.Created.Count == starts && HasWallpaper(controller, LeftId, "living-fire"), "Browsing the library changed a monitor's wallpaper.");

            var rightBefore = controller.ActiveRequests[RightId].Settings;
            var leftPreferences = new VisualizerPreferences(4.6f, 6.1f, 1.8f, 2, 1.35f, -0.3f, 0.2f);
            SelectWallpaper(window, "living-fire");
            SetPreferences(window, leftPreferences);
            Assert(controller.ActiveRequests[LeftId].Settings == leftPreferences.ToSettings(), "Customization did not reach the selected display.");
            Assert(controller.ActiveRequests[RightId].Settings == rightBefore, "Customization leaked into a different wallpaper on another display.");

            SelectDisplay(window, 1);
            SelectWallpaper(window, "living-fire");
            Click(window, "StartButton");
            WaitFor(() => HasWallpaper(controller, RightId, "living-fire"), window, "Apply same wallpaper independently");
            var rightPreferences = new VisualizerPreferences(1.7f, 3.2f, 0.4f, 1, 0.8f, 0.4f, -0.25f, Sparks: false);
            SetPreferences(window, rightPreferences);
            Assert(controller.ActiveRequests[LeftId].Settings == leftPreferences.ToSettings(), "Same-wallpaper customization leaked into the other display.");
            SelectDisplay(window, 0);
            Assert(CurrentPreferences(window) == leftPreferences, "Switching displays lost their independent preferences.");
            SelectDisplay(window, 1);
            Assert(CurrentPreferences(window) == rightPreferences, "Second display did not reload its independent preferences.");

            starts = factory.Created.Count;
            Click(window, "ApplyAllButton");
            WaitFor(() => factory.Created.Count >= starts + 2 && !IsChanging(window), window, "Apply to all displays");
            Assert(controller.ActiveRequests.Count == 2 && controller.ActiveRequests.Values.All(request => request.Id == "living-fire"), "Apply to all did not apply the selected wallpaper to both displays.");
            Assert(controller.ActiveRequests.Values.All(request => request.Settings == rightPreferences.ToSettings()), "Apply to all did not copy the selected display's customization.");
            SelectDisplay(window, 0);
            var editedLeft = leftPreferences with { Scale = 1.75f, ColorTheme = 3 };
            SetPreferences(window, editedLeft);
            Assert(controller.ActiveRequests[RightId].Settings == rightPreferences.ToSettings(), "Editing after Apply to all kept the monitors linked.");
            Assert(controller.ActiveRequests[LeftId].Settings == editedLeft.ToSettings(), "Editing after Apply to all did not update the selected monitor.");

            Capture(window, output, "display-library-wide", 1280, 780);
            Assert(Element<FrameworkElement>(window, "PreviewPanel").Visibility == Visibility.Visible, "Wide display library did not dock the preview.");
            AssertActionsFit(window, 1280, 780);
            Capture(window, output, "display-library-compact", 760, 620);
            Assert(Element<FrameworkElement>(window, "PreviewPanel").Visibility == Visibility.Collapsed, "Compact display library did not release preview space.");
            AssertActionsFit(window, 760, 620);
            Capture(window, output, "display-library-small", 640, 520);
            AssertActionsFit(window, 640, 520);

            Click(window, "StopDisplayButton");
            Pump();
            Assert(!controller.ActiveRequests.ContainsKey(LeftId) && HasWallpaper(controller, RightId, "living-fire"), "Stop this display stopped another display or left its own session running.");
            Invoke(window, "SavePreferences");
            var saved = store.Load();
            Assert(!saved.DisplayWallpapers[LeftId].Enabled && saved.DisplayWallpapers[RightId].Enabled, "Stop this display did not persist independent enabled states.");
            Assert(saved.DisplayWallpapers[LeftId].Visualizers["living-fire"] == editedLeft, "Stopping erased the monitor's customization.");
            Console.WriteLine("PASS: UI per-display Apply, selection/active badges, browsing isolation, independent customization, Apply to all, Stop this display, responsive monitor controls");
        }
        finally { Close(window); }

        factory = new SessionFactory();
        controller = new DisplayWallpaperController(factory.CreateAsync);
        window = CreateWindow(controller);
        try
        {
            RunTask(window, "RestoreMissingDisplaysAsync");
            Assert(controller.ActiveRequests.Count == 1 && HasWallpaper(controller, RightId, "living-fire"), "Restart did not restore only enabled monitor assignments.");
            Assert(factory.Created.Count == 1, "Restart created a disabled monitor's session.");
            SelectDisplay(window, 0);
            SelectWallpaper(window, "event-horizon");
            var recoveredPreferences = new VisualizerPreferences(5.4f, 7, 2.1f, 2, 1.6f, 0.2f, -0.1f);
            SetPreferences(window, recoveredPreferences);
            Click(window, "StartButton");
            WaitFor(() => HasWallpaper(controller, LeftId, "event-horizon"), window, "Apply before disconnect");
            var leftSession = factory.Created.Last(session => session.Request.Target?.DeviceId == LeftId);

            connected = [Right];
            Invoke(window, "RememberDisplays");
            RunTask(window, "RecoverAsync");
            Assert(leftSession.Disposed && !controller.ActiveRequests.ContainsKey(LeftId), "Disconnect did not remove the departed display's session.");
            Assert(HasWallpaper(controller, RightId, "living-fire"), "Disconnect interrupted the remaining display's wallpaper.");
            Invoke(window, "SavePreferences");
            var unplugged = store.Load();
            Assert(unplugged.DisplayWallpapers[LeftId].Enabled && unplugged.DisplayWallpapers[LeftId].WallpaperId == "event-horizon", "Disconnect discarded the saved assignment.");
            Assert(unplugged.DisplayWallpapers[LeftId].Visualizers["event-horizon"] == recoveredPreferences, "Disconnect discarded saved customization.");

            // Reverse enumeration and change bounds/DPI; stable IDs, not array indices,
            // must decide which session and preference set belongs on each monitor.
            var movedLeft = Left with { X = 1080, Y = 0, Width = 3840, Height = 2160, DpiX = 192, DpiY = 192 };
            connected = [Right, movedLeft];
            Invoke(window, "RememberDisplays");
            RunTask(window, "RecoverAsync");
            Assert(controller.ActiveRequests.Count == 2 && HasWallpaper(controller, LeftId, "event-horizon") && HasWallpaper(controller, RightId, "living-fire"), "Reconnect assigned wallpapers by index instead of stable device ID.");
            Assert(controller.ActiveRequests[LeftId].Target == movedLeft, "Reconnect did not apply the monitor's new bounds and DPI.");
            Assert(controller.ActiveRequests[LeftId].Settings == recoveredPreferences.ToSettings(), "Reconnect lost the display's customization.");
            SelectDisplay(window, 1);
            Assert(SelectedWallpaper(window).Id == "event-horizon", "Monitor selection after reorder restored the wrong wallpaper card.");
            Assert(CurrentPreferences(window) == recoveredPreferences, "Monitor selection after reconnect loaded another monitor's preferences.");

            Click(window, "StopButton");
            Pump();
            Invoke(window, "SavePreferences");
            Assert(controller.ActiveRequests.Count == 0 && store.Load().DisplayWallpapers.Values.All(value => !value.Enabled), "Stop all did not stop and disable every assignment.");
            Console.WriteLine("PASS: restart restores enabled assignments only, unplug/replug retains independent wallpapers, reordered mixed-DPI displays use stable IDs, Stop all persists");
        }
        finally { Close(window); }

        Assert(store.Save(new AppSettings
        {
            AppPauseMode = 0,
            AudioReactive = false,
            DisplayWallpapers = new(StringComparer.OrdinalIgnoreCase)
            {
                [LeftId] = new() { Enabled = true, WallpaperId = "living-fire" },
                [RightId] = new() { Enabled = true, WallpaperId = "event-horizon" }
            }
        }), "Could not initialize recovery isolation settings.");
        connected = [Left];
        factory = new SessionFactory();
        controller = new DisplayWallpaperController(factory.CreateAsync);
        window = CreateWindow(controller);
        try
        {
            RunTask(window, "RestoreMissingDisplaysAsync");
            Assert(HasWallpaper(controller, LeftId, "living-fire") && !controller.DesiredRequests.ContainsKey(RightId),
                "Initially disconnected display unexpectedly gained a runtime assignment.");
            factory.Created.Single().Healthy = false;
            factory.FailedDisplayIds.Add(LeftId);
            connected = [Left, Right];
            Invoke(window, "RememberDisplays");
            RunTask(window, "RecoverAsync");
            Assert(HasWallpaper(controller, RightId, "event-horizon"),
                "Another display's recovery failure prevented restoration of a newly connected saved display.");
            Assert(HasWallpaper(controller, LeftId, "living-fire") && !factory.Created[0].Disposed,
                "A failed replacement discarded the existing display's session.");
            Assert(!string.IsNullOrWhiteSpace(Element<System.Windows.Controls.TextBlock>(window, "ErrorText").Text),
                "Recovery isolation hid the failed display's error.");
            Click(window, "StopButton");
            RunTask(window, "RecoverAsync");
            Assert(controller.ActiveRequests.Count == 0 && controller.DesiredRequests.Count == 0,
                "Recovery after Stop all restored an assignment.");
            Console.WriteLine("PASS: failed display recovery does not block newly connected saved displays; Stop all cancels recovery");
        }
        finally { Close(window); }

        MainWindow CreateWindow(DisplayWallpaperController controller)
            => new(store, Path.Combine(output, "display-window-library"), controller,
                new AppLicenseService(new UnmanagedProvider()), () => connected);
    }

    private static void SelectDisplay(MainWindow window, int index)
    {
        var target = Element<ComboBox>(window, "TargetDisplayCombo");
        target.SelectedIndex = index;
        Pump();
        Assert(ReferenceEquals(target.SelectedItem, Element<ComboBox>(window, "PreviewDisplayCombo").SelectedItem), "Main and preview display selectors diverged.");
    }

    private static void SelectWallpaper(MainWindow window, string id)
    {
        var gallery = Element<ListBox>(window, "WallpaperGallery");
        gallery.SelectedItem = gallery.Items.Cast<WallpaperEntry>().Single(entry => entry.Id == id);
        Pump();
    }

    private static WallpaperEntry SelectedWallpaper(MainWindow window) => (WallpaperEntry)Element<ListBox>(window, "WallpaperGallery").SelectedItem;
    private static VisualizerPreferences CurrentPreferences(MainWindow window)
        => (VisualizerPreferences)Invoke(window, "CurrentPreferencesFor", SelectedWallpaper(window))!;
    private static void SetPreferences(MainWindow window, VisualizerPreferences preferences)
    {
        Invoke(window, "ApplyVisualizerPreferences", preferences);
        Pump();
    }
    private static bool HasWallpaper(DisplayWallpaperController controller, string display, string wallpaper)
        => controller.ActiveRequests.TryGetValue(display, out var request) && request.Id == wallpaper;
    private static bool IsChanging(MainWindow window)
        => (bool)typeof(MainWindow).GetField("_changingWallpaper", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static T Element<T>(MainWindow window, string name) where T : FrameworkElement
        => (T)(window.FindName(name) ?? throw new InvalidOperationException($"Missing {name} control."));
    private static object? Invoke(MainWindow window, string name, params object?[] arguments)
        => (typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing {name} method.")).Invoke(window, arguments);

    private static void Click(MainWindow window, string name)
    {
        var button = Element<Button>(window, name);
        Assert(button.IsEnabled, $"{name} unexpectedly disabled.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private static void RunTask(MainWindow window, string name)
    {
        var task = (Task)Invoke(window, name)!;
        WaitFor(() => task.IsCompleted, window, name);
        task.GetAwaiter().GetResult();
    }

    private static void WaitFor(Func<bool> completed, MainWindow window, string operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (!completed() && elapsed.Elapsed < TimeSpan.FromSeconds(8)) Pump();
        var error = Element<System.Windows.Controls.TextBlock>(window, "ErrorText").Text;
        Assert(completed(), $"Timed out waiting for {operation}. UI error: {error}");
        Pump();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Close(MainWindow window)
    {
        typeof(MainWindow).GetField("_isQuitting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
        window.Close();
        Pump();
    }

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

    private static void AssertActionsFit(MainWindow window, int width, int height)
    {
        var root = (FrameworkElement)window.Content;
        foreach (var name in new[] { "TargetDisplayCombo", "StartButton", "ApplyAllButton", "StopDisplayButton", "StopButton" })
        {
            var element = Element<FrameworkElement>(window, name);
            Assert(element.Visibility == Visibility.Visible && element.ActualWidth > 1 && element.ActualHeight > 1, $"{name} is absent at {width}x{height}.");
            var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
            Assert(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1,
                $"{name} is outside the {width}x{height} window: {bounds}.");
            for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element); ancestor is not null && !ReferenceEquals(ancestor, root); ancestor = VisualTreeHelper.GetParent(ancestor))
            {
                if (ancestor is not FrameworkElement parent) continue;
                Assert(parent.Visibility == Visibility.Visible, $"{name} is hidden by an ancestor at {width}x{height}.");
                if (!parent.ClipToBounds) continue;
                var local = element.TransformToAncestor(parent).TransformBounds(new Rect(element.RenderSize));
                Assert(local.Left >= -1 && local.Top >= -1 && local.Right <= parent.ActualWidth + 1 && local.Bottom <= parent.ActualHeight + 1,
                    $"{name} is clipped by {parent.GetType().Name} at {width}x{height}.");
            }
        }
    }

    private sealed class SessionFactory
    {
        public List<Session> Created { get; } = [];
        public HashSet<string> FailedDisplayIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<IWallpaperSession> CreateAsync(WallpaperRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<IWallpaperSession>();
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                if (token.IsCancellationRequested) { completion.SetCanceled(token); return; }
                if (request.Target is { } target && FailedDisplayIds.Contains(target.DeviceId))
                {
                    completion.SetException(new IOException($"Simulated session preparation failure on {target.DeviceId}."));
                    return;
                }
                var session = new Session(request);
                Created.Add(session);
                completion.SetResult(session);
            }), DispatcherPriority.Background);
            return completion.Task;
        }
    }

    private sealed class Session(WallpaperRequest request) : IWallpaperSession
    {
        public WallpaperRequest Request { get; } = request;
        public bool Disposed { get; private set; }
        public bool Healthy { get; set; } = true;
        public int? ProcessId => null;
        public bool IsHealthy => !Disposed && Healthy;
        public void Show() { }
        public void SetFrameCap(int framesPerSecond) { }
        public void Resume() { }
        public void Pause() { }
        public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
        public void SetAudioEnabled(bool enabled) { }
        public void Dispose() => Disposed = true;
    }

    private sealed class UnmanagedProvider : IAppLicenseProvider
    {
        public bool IsStoreManaged => false;
        public event Action? LicenseChanged { add { } remove { } }
        public void Initialize(IntPtr owner) { }
        public Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken token) => Task.FromResult(new AppLicenseSnapshot(AppLicenseKind.Unmanaged));
        public Task<string?> GetPriceAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task<AppPurchaseResult> PurchaseAsync(CancellationToken token) => throw new InvalidOperationException("UI display tests must not request a purchase.");
        public void Dispose() { }
    }
}
