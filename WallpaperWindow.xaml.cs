using System.Windows;
using System.Windows.Interop;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class WallpaperWindow : Window
{
    private readonly int _initialFramesPerSecond;
    private AnimationSourceWindow? _sourceWindow;
    private DwmThumbnailProjection? _projection;

    public WallpaperWindow(int targetFramesPerSecond)
    {
        InitializeComponent();
        _initialFramesPerSecond = targetFramesPerSecond;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    public void SetFrameCap(int framesPerSecond)
    {
        _sourceWindow?.SetFrameCap(framesPerSecond);
    }

    public void Resume()
    {
        _sourceWindow?.Resume();
    }

    public void Pause()
    {
        _sourceWindow?.Pause();
    }

    protected override void OnClosed(EventArgs e)
    {
        _projection?.Dispose();
        _projection = null;
        _sourceWindow?.Close();
        _sourceWindow = null;
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        AppLog.Write($"WallpaperWindow loaded. Handle=0x{handle.ToInt64():X}; WPF bounds={Left},{Top},{Width}x{Height}");
        _sourceWindow = new AnimationSourceWindow(_initialFramesPerSecond);
        _sourceWindow.Show();
        _sourceWindow.Resume();

        var sourceHandle = new WindowInteropHelper(_sourceWindow).Handle;
        _projection = new DwmThumbnailProjection(sourceHandle, handle);
        DesktopWorker.AttachWallpaperWindow(handle);
        _projection.FillDestination(
            (int)Math.Round(SystemParameters.VirtualScreenWidth * GetDesktopScale()),
            (int)Math.Round(SystemParameters.VirtualScreenHeight * GetDesktopScale()));
        AppLog.Write($"Wallpaper rendering started through DWM. Source=0x{sourceHandle.ToInt64():X}");
    }

    private static double GetDesktopScale() =>
        System.Windows.Media.VisualTreeHelper.GetDpi(System.Windows.Application.Current.MainWindow).DpiScaleX;
}
