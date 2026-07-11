using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow : Window
{
    private const string ExampleVideoPath = @"D:\LiveWallpaper\68ae669b92d000f8170811b1\68ae669b92d000f8170811b1.mp4";
    private readonly WallpaperController _wallpaperController = new();
    private readonly ForegroundAppMonitor _foregroundMonitor = new();
    private readonly WallpaperLibraryService _wallpaperLibrary = new();
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private bool _isUiInitialized;
    private bool _isQuitting;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();
        _isUiInitialized = true;
        UpdateVisualizerSettingsVisibility();
        PreviewBackdrop.Start();
        _foregroundMonitor.StateChanged += (_, _) => ApplyPlaybackPolicy();
        _foregroundMonitor.Start();
        UpdateStatus();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _trayDrawingIcon?.Dispose();
        PreviewBackdrop.Dispose();
        _foregroundMonitor.Dispose();
        _wallpaperLibrary.Dispose();
        _wallpaperController.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            Hide();
            AppLog.Write("Main window hidden to notification area; wallpaper remains active");

            if (!_trayHintShown && _trayIcon is not null)
            {
                _trayHintShown = true;
                _trayIcon.ShowBalloonTip(
                    2500,
                    "HYPNIX is still running",
                    "Double-click the tray icon to reopen it, or use Quit HYPNIX to exit.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }

            return;
        }

        base.OnClosing(e);
    }

    private void InitializeTrayIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "hypnix-tray-white.ico");
        _trayDrawingIcon = System.IO.File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = menu.Items.Add("Open HYPNIX");
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        menu.Items.Add("Stop wallpaper", null, (_, _) => Dispatcher.Invoke(StopWallpaperFromTray));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit HYPNIX", null, (_, _) => Dispatcher.Invoke(QuitApplication));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = "HYPNIX - Animated wallpapers",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
        AppLog.Write($"Notification-area icon initialized. Icon={iconPath}");
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        AppLog.Write("Main window restored from notification area");
    }

    private void StopWallpaperFromTray()
    {
        AppLog.Write("Stop wallpaper selected from notification area");
        _wallpaperController.Stop();
        _foregroundMonitor.ExcludedProcessId = null;
        _foregroundMonitor.RefreshNow();
        UpdateStatus();
    }

    private void QuitApplication()
    {
        AppLog.Write("Quit HYPNIX selected from notification area");
        _isQuitting = true;
        Close();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkTitleBar();
        // SystemParameters.WorkArea is the primary monitor's usable area in WPF
        // device-independent pixels, so it already accounts for DPI and taskbar.
        var workArea = SystemParameters.WorkArea;
        const double safeMargin = 32;
        Width = Math.Min(1180, Math.Max(MinWidth, workArea.Width - safeMargin * 2));
        Height = Math.Min(760, Math.Max(MinHeight, workArea.Height - safeMargin * 2));
        Left = workArea.Left + Math.Max(safeMargin, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(safeMargin, (workArea.Height - Height) / 2);
        AppLog.Write($"Main window placed on primary work area. WorkArea={workArea.Left},{workArea.Top}," +
                     $"{workArea.Width},{workArea.Height}; Window={Left},{Top},{Width},{Height}");
    }

    private void ApplyDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        var result = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        if (result != 0) DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        AppLog.Write($"Dark title bar requested. Handle=0x{handle.ToInt64():X}; result={result}");
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Write($"Start clicked. FPS={GetSelectedFps()}");
        StatusText.Text = "Preparing wallpaper...";
        StartButton.IsEnabled = false;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        try
        {
            _wallpaperController.Start(GetSelectedFps(), GetSelectedWallpaperKind(), ExampleVideoPath);
            ApplyVisualizerSettings();
            _foregroundMonitor.ExcludedProcessId = _wallpaperController.ActiveProcessId;
            _foregroundMonitor.RefreshNow();
            ApplyPlaybackPolicy();
            AppLog.Write($"Start completed. Running={_wallpaperController.IsRunning}; Paused={_wallpaperController.IsPaused}");
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Start failed", exception);
            StatusText.Text = "Start failed - check logs";
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        AppLog.Write("Stop clicked");
        _wallpaperController.Stop();
        _foregroundMonitor.ExcludedProcessId = null;
        _foregroundMonitor.RefreshNow();
        UpdateStatus();
    }

    private void PolicyChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized)
        {
            return;
        }

        ApplyPlaybackPolicy();
    }

    private void PolicyModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized)
        {
            return;
        }

        ApplyPlaybackPolicy();
    }

    private void FpsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var fps = GetSelectedFps();
        PreviewBackdrop.TargetFramesPerSecond = fps;
        _wallpaperController.SetFrameCap(fps);
    }

    private async void WallpaperComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiInitialized) UpdateVisualizerSettingsVisibility();
        if (!_isUiInitialized || !_wallpaperController.IsRunning) return;
        AppLog.Write($"Wallpaper selection changed. Kind={GetSelectedWallpaperKind()}");
        await StartSelectedWallpaperAsync();
    }

    private async Task StartSelectedWallpaperAsync()
    {
        StatusText.Text = "Switching wallpaper...";
        StartButton.IsEnabled = false;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        try
        {
            _wallpaperController.Start(GetSelectedFps(), GetSelectedWallpaperKind(), ExampleVideoPath);
            ApplyVisualizerSettings();
            _foregroundMonitor.ExcludedProcessId = _wallpaperController.ActiveProcessId;
            _foregroundMonitor.RefreshNow();
            ApplyPlaybackPolicy();
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Wallpaper switch failed", exception);
            StatusText.Text = "Switch failed - check logs";
        }
        finally
        {
            StartButton.IsEnabled = true;
        }
    }

    private int GetSelectedFps()
    {
        return FpsComboBox.SelectedItem is ComboBoxItem item &&
               int.TryParse(item.Tag?.ToString(), out var requested)
            ? FrameRatePolicy.Normalize(requested)
            : 30;
    }

    private WallpaperKind GetSelectedWallpaperKind() => WallpaperComboBox.SelectedIndex switch
    {
        1 => WallpaperKind.VisualizerDemo,
        2 => WallpaperKind.ExampleVideo,
        _ => WallpaperKind.BuiltIn
    };

    private void VisualizerSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        VisualizerSettingsPopup.IsOpen = !VisualizerSettingsPopup.IsOpen;
    }

    private void VisualizerSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUiInitialized) ApplyVisualizerSettings();
    }

    private void VisualizerColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiInitialized) ApplyVisualizerSettings();
    }

    private void UpdateVisualizerSettingsVisibility()
    {
        var isVisualizer = GetSelectedWallpaperKind() == WallpaperKind.VisualizerDemo;
        VisualizerSettingsButton.Visibility = isVisualizer ? Visibility.Visible : Visibility.Collapsed;
        if (!isVisualizer) VisualizerSettingsPopup.IsOpen = false;
        UpdateWallpaperSelectionVisuals();
    }

    private void AmbientCard_Click(object sender, RoutedEventArgs e) => WallpaperComboBox.SelectedIndex = 0;

    private void VisualizerCard_Click(object sender, RoutedEventArgs e) => WallpaperComboBox.SelectedIndex = 1;

    private void VideoCard_Click(object sender, RoutedEventArgs e) => WallpaperComboBox.SelectedIndex = 2;

    private void UpdateWallpaperSelectionVisuals()
    {
        if (AmbientCardBorder is null || VisualizerCardBorder is null || VideoCardBorder is null) return;
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var transparent = System.Windows.Media.Brushes.Transparent;
        AmbientCardBorder.BorderBrush = WallpaperComboBox.SelectedIndex == 0 ? accent : transparent;
        VisualizerCardBorder.BorderBrush = WallpaperComboBox.SelectedIndex == 1 ? accent : transparent;
        VideoCardBorder.BorderBrush = WallpaperComboBox.SelectedIndex == 2 ? accent : transparent;

        (ActivePreviewTitle.Text, ActivePreviewSubtitle.Text) = WallpaperComboBox.SelectedIndex switch
        {
            1 => ("Audio visualizer", "WASAPI loopback · 64 FFT bands · two displays"),
            2 => ("Example MP4", "Local video · per-display aspect correction"),
            _ => ("Built-in ambient", "Native procedural wallpaper")
        };
        var previewPath = WallpaperComboBox.SelectedIndex switch
        {
            1 => "Assets/Wallpapers/audio-visualizer-classic/preview.png",
            2 => "Assets/Wallpapers/example-video/preview.jpg",
            _ => "Assets/Wallpapers/built-in-ambient/preview.jpg"
        };
        SelectedPreviewImage.Source = new BitmapImage(new Uri($"pack://application:,,,/{previewPath}"));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private void ApplyVisualizerSettings()
    {
        if (GetSelectedWallpaperKind() != WallpaperKind.VisualizerDemo) return;
        var (startColor, endColor) = VisualizerColorComboBox.SelectedIndex switch
        {
            1 => (System.Drawing.Color.FromArgb(255, 82, 120), System.Drawing.Color.FromArgb(255, 185, 70)),
            2 => (System.Drawing.Color.FromArgb(70, 255, 185), System.Drawing.Color.FromArgb(20, 160, 120)),
            3 => (System.Drawing.Color.FromArgb(245, 245, 250), System.Drawing.Color.FromArgb(120, 130, 150)),
            _ => (System.Drawing.Color.FromArgb(88, 205, 255), System.Drawing.Color.FromArgb(104, 80, 255))
        };
        _wallpaperController.UpdateVisualizerSettings(new VisualizerSettings(
            (float)VisualizerIntensitySlider.Value,
            (float)VisualizerSensitivitySlider.Value,
            (float)VisualizerGlowSlider.Value,
            startColor,
            endColor));
    }

    private void ApplyPlaybackPolicy()
    {
        var appPauseMode = AppPauseModeComboBox.SelectedIndex;
        var appShouldPause =
            appPauseMode == 2 && _foregroundMonitor.IsOtherAppActive ||
            appPauseMode == 1 && _foregroundMonitor.IsFullscreenActive;
        var batteryShouldPause = PauseBatteryCheckBox.IsChecked == true && _foregroundMonitor.IsOnBattery;

        if (!_wallpaperController.IsRunning)
        {
            UpdateStatus();
            return;
        }

        if (appShouldPause && PauseScopeComboBox.SelectedIndex == 1 &&
            _foregroundMonitor.ForegroundMonitorIndex is int monitorIndex)
        {
            _wallpaperController.Resume();
            _wallpaperController.SetPausedMonitor(monitorIndex);
        }
        else if (appShouldPause || batteryShouldPause)
        {
            _wallpaperController.SetPausedMonitor(null);
            _wallpaperController.Pause();
        }
        else
        {
            _wallpaperController.SetPausedMonitor(null);
            _wallpaperController.Resume();
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var state = _wallpaperController.IsRunning
            ? _wallpaperController.IsPaused ? "Paused" : "Running"
            : "Stopped";

        var appPauseMode = AppPauseModeComboBox.SelectedIndex;
        var perDisplayPause = PauseScopeComboBox.SelectedIndex == 1 &&
                              _foregroundMonitor.ForegroundMonitorIndex is int;
        var reason = appPauseMode == 2 && _foregroundMonitor.IsOtherAppActive
            ? perDisplayPause
                ? $"display {_foregroundMonitor.ForegroundMonitorIndex!.Value + 1} paused"
                : "another app is active"
            : appPauseMode == 1 && _foregroundMonitor.IsFullscreenActive
            ? perDisplayPause
                ? $"display {_foregroundMonitor.ForegroundMonitorIndex!.Value + 1} paused"
                : "fullscreen app detected"
            : _foregroundMonitor.IsOnBattery ? "battery power" : "ready";

        StatusText.Text = $"{state} - {reason}";
    }
}
