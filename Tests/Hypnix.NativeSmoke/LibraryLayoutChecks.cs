using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using Size = System.Windows.Size;

internal static class LibraryLayoutChecks
{
    public static void Run(string output)
    {
        var starts = 0;
        var controller = new DisplayWallpaperController((_, _) => { starts++; return Task.FromResult<IWallpaperSession>(new Session()); });
        var store = new AppSettingsStore(Path.Combine(output, "layout-settings.json"));
        store.Save(new AppSettings());
        var window = new MainWindow(store, Path.Combine(output, "layout-library"), controller);
        var root = (FrameworkElement)window.Content;
        var gallery = (System.Windows.Controls.ListBox)window.FindName("WallpaperGallery");
        try
        {
            gallery.SelectedIndex = 1;
            Invoke("StartButton_Click");
            var active = controller.ActiveRequests.Values.FirstOrDefault()?.Id;
            gallery.SelectedIndex = 2;
            if (starts != 1 || controller.ActiveRequests.Values.FirstOrDefault()?.Id != active || window.ActiveWallpaperId != active)
                throw new Exception("Browsing replaced the desktop or lost the active badge.");
            var selected = gallery.SelectedItem;
            Capture("library-wide", 1280, 780);
            if (Element("PreviewPanel").Visibility != Visibility.Visible || Element("LibraryPanel").Visibility != Visibility.Visible)
                throw new Exception("Wide layout did not dock the preview.");
            Capture("library-compact", 760, 620);
            if (Element("PreviewPanel").Visibility != Visibility.Collapsed) throw new Exception("Compact library lost its browsing space.");
            var scroller = Descendants(gallery).OfType<ScrollViewer>().First();
            scroller.ScrollToVerticalOffset(180); root.UpdateLayout();
            var offset = scroller.VerticalOffset;
            Invoke("Preview_Click"); Capture("preview-compact", 760, 620);
            if (Element("LibraryPanel").Visibility != Visibility.Collapsed) throw new Exception("Compact preview did not use a dedicated page.");
            ((AnimatedWallPaper.Controls.AspectRatioDecorator)window.FindName("PreviewAspect")).AspectRatio = 9d / 16;
            Capture("preview-portrait", 760, 620);
            Invoke("ClosePreview_Click");
            root.UpdateLayout();
            if (!ReferenceEquals(selected, gallery.SelectedItem)) throw new Exception("Returning from preview lost selection.");
            if (Math.Abs(scroller.VerticalOffset - offset) > 1) throw new Exception("Returning from preview lost library scroll position.");
            Invoke("AppSettings_Click"); Capture("app-settings", 760, 620);
            if (Element("LibraryWorkspace").Visibility != Visibility.Collapsed) throw new Exception("App settings left the live host visible.");
            Invoke("BackToLibrary_Click");
            Capture("library-small", 640, 520);
            if (starts != 1) throw new Exception("Preview navigation changed the desktop.");
            Invoke("StopButton_Click");
            if (window.ActiveWallpaperId is not null) throw new Exception("Stopping left an active wallpaper badge.");
            Console.WriteLine("PASS: responsive library, dedicated preview, portrait framing, settings navigation, explicit Apply and active/selected distinction");
        }
        finally
        {
            typeof(MainWindow).GetField("_isQuitting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, true);
            window.Close();
        }
        FrameworkElement Element(string name) => (FrameworkElement)window.FindName(name);
        void Invoke(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window, new RoutedEventArgs()]);
        void Capture(string name, int width, int height)
        {
            var size = new Size(width, height);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class Session : IWallpaperSession
    {
        public int? ProcessId => null;
        public bool IsHealthy => true;
        public void Show() { }
        public void SetFrameCap(int framesPerSecond) { }
        public void Resume() { }
        public void Pause() { }
        public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
        public void SetAudioEnabled(bool enabled) { }
        public void Dispose() { }
    }
}
