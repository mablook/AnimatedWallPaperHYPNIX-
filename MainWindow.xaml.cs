using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow : Window
{
    private readonly WallpaperController _wallpaperController = new();
    private readonly ForegroundAppMonitor _foregroundMonitor = new();
    private readonly WallpaperLibraryService _wallpaperLibrary;
    private readonly DesktopEnvironmentMonitor _environment = new();
    private readonly AppSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayDrawingIcon;
    private bool _isUiInitialized;
    private bool _isQuitting;
    private bool _trayHintShown;
    private bool _changingWallpaper;
    private bool _recovering;
    private bool _playRequested;
    private int _selectionRevision;
    private int _recoveryAttempts;
    private WallpaperEntry? Selected => WallpaperGallery.SelectedItem as WallpaperEntry;

    public MainWindow() : this(new AppSettingsStore(), null) { }

    internal MainWindow(AppSettingsStore settingsStore, string? libraryRoot)
    {
        _settingsStore = settingsStore;
        _wallpaperLibrary = new WallpaperLibraryService(libraryRoot);
        _settings = _settingsStore.Load();
        InitializeComponent();
        InitializeTrayIcon();
        AppPauseModeComboBox.SelectedIndex = _settings.AppPauseMode;
        PauseScopeComboBox.SelectedIndex = _settings.PausePerMonitor ? 1 : 0;
        PauseBatteryCheckBox.IsChecked = _settings.PauseOnBattery;
        FpsComboBox.SelectedIndex = _settings.FramesPerSecond == 15 ? 0 : _settings.FramesPerSecond == 60 ? 2 : 1;
        RefreshGallery();
        _isUiInitialized = true;
        UpdateSelection();
        _saveTimer.Tick += (_, _) => SavePreferences();
        _recoveryTimer.Tick += async (_, _) => { _recoveryTimer.Stop(); await RecoverAsync(); };
        _wallpaperLibrary.PackagesChanged += OnPackagesChanged;
        _foregroundMonitor.StateChanged += (_, _) => ApplyPlaybackPolicy();
        _wallpaperController.StateChanged += () => ApplyPlaybackPolicy();
        _environment.PolicyChanged += ApplyPlaybackPolicy;
        _environment.LayoutChanged += () => { RememberDisplays(); _recoveryAttempts = 0; ScheduleRecovery(); };
        _environment.HealthCheck += () =>
        {
            if (_wallpaperController.IsRunning && !_wallpaperController.IsHealthy) ScheduleRecovery();
        };
        LivePreview.StatusChanged += status => PreviewStatusText.Text = status;
        IsVisibleChanged += (_, _) => UpdatePreviewSuspension();
        StateChanged += (_, _) => UpdatePreviewSuspension();
        _foregroundMonitor.Start();
        RememberDisplays();
        UpdateStatus();
    }

    private void OnPackagesChanged(IReadOnlyList<WallpaperPackageRegistration> packages)
        => Dispatcher.BeginInvoke(new Action(() => { if (!_isQuitting) RefreshGallery(); }));

    private void RefreshGallery()
    {
        var selectedId = Selected?.Id ?? _settings.SelectedWallpaperId;
        var initialized = _isUiInitialized;
        _isUiInitialized = false;
        var entries = WallpaperCatalog.LoadBuiltIns().ToList();
        var invalid = 0;
        foreach (var package in _wallpaperLibrary.Packages)
        {
            try
            {
                if (package.Status == WallpaperPackageStatus.Ready) entries.Add(WallpaperCatalog.FromPackage(package));
                else invalid++;
            }
            catch (Exception exception)
            {
                invalid++;
                AppLog.WriteException("Library preset could not be loaded", exception);
            }
        }
        entries.AddRange(_settings.Videos.Select(video => new WallpaperEntry(video.Id, video.Title,
            WallpaperKind.ExampleVideo, null, VideoPath: video.Path)));
        WallpaperGallery.ItemsSource = entries;
        WallpaperGallery.SelectedItem = entries.FirstOrDefault(entry => entry.Id == selectedId) ?? entries.FirstOrDefault();
        LibraryStatusText.Text = $"{entries.Count} wallpapers · {invalid} unavailable packages";
        _isUiInitialized = initialized;
        if (initialized) UpdateSelection();
    }

    private async void WallpaperGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized) return;
        UpdateSelection();
        QueueSave();
        if (_playRequested) await StartSelectedAsync();
    }

    private void UpdateSelection()
    {
        if (Selected is not { } entry) { LivePreview.Select(null); return; }
        _settings.SelectedWallpaperId = entry.Id;
        ActivePreviewTitle.Text = entry.Title;
        ActivePreviewSubtitle.Text = entry.Description;
        VisualizerSettingsButton.Visibility = entry.IsVisualizer ? Visibility.Visible : Visibility.Collapsed;
        VisualizerSettingsPopup.IsOpen = false;
        var initialized = _isUiInitialized;
        _isUiInitialized = false;
        var value = _settings.Visualizers.GetValueOrDefault(entry.Id) ?? entry.Defaults ?? new();
        VisualizerIntensitySlider.Value = value.Intensity;
        VisualizerSensitivitySlider.Value = value.Sensitivity;
        VisualizerGlowSlider.Value = value.Glow;
        VisualizerColorComboBox.SelectedIndex = value.ColorTheme;
        _isUiInitialized = initialized;
        try { LivePreview.Select(WallpaperCatalog.Request(entry, _settings)); }
        catch (Exception exception) { LivePreview.Select(null); ReportError("Preview", exception); }
        UpdatePreviewSuspension();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _playRequested = true;
        await StartSelectedAsync();
    }

    private async Task StartSelectedAsync()
    {
        if (Selected is not { } entry) return;
        var revision = ++_selectionRevision;
        _changingWallpaper = true;
        _recoveryAttempts = 0;
        _recoveryTimer.Stop();
        StartButton.IsEnabled = false;
        ErrorText.Text = "";
        StatusText.Text = "Preparing wallpaper…";
        try
        {
            await _wallpaperController.StartAsync(WallpaperCatalog.Request(entry, _settings));
            _wallpaperController.SetFrameCap(GetSelectedFps());
            ApplyVisualizerSettings();
            _foregroundMonitor.ExcludedProcessId = _wallpaperController.ActiveProcessId;
            _foregroundMonitor.RefreshNow();
            ApplyPlaybackPolicy();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (revision == _selectionRevision)
            {
                _playRequested = _wallpaperController.IsRunning;
                ReportError("Wallpaper could not be started", exception);
            }
        }
        finally
        {
            if (revision == _selectionRevision)
            {
                _changingWallpaper = false;
                StartButton.IsEnabled = true;
                UpdateStatus();
            }
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopWallpaper();
    private void StopWallpaper()
    {
        _playRequested = false;
        _selectionRevision++;
        _changingWallpaper = false;
        _recoveryTimer.Stop();
        _wallpaperController.Stop();
        _foregroundMonitor.ExcludedProcessId = null;
        StartButton.IsEnabled = true;
        UpdateStatus();
    }

    private void ScheduleRecovery()
    {
        if (_isQuitting || !_playRequested || _changingWallpaper || _recovering || _recoveryTimer.IsEnabled) return;
        _recoveryTimer.Start();
    }

    private async Task RecoverAsync()
    {
        if (_isQuitting || !_playRequested || _changingWallpaper || _recovering || _wallpaperController.ActiveRequest is not { } request) return;
        _recovering = true;
        try
        {
            AppLog.Write("Rebuilding wallpaper after desktop/display/decoder change");
            await _wallpaperController.StartAsync(request);
            _recoveryAttempts = 0;
            _foregroundMonitor.RefreshNow();
            ApplyPlaybackPolicy();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ReportError("Desktop recovery", exception);
            if (++_recoveryAttempts >= 3)
            {
                StopWallpaper();
                ErrorText.Text += " Playback stopped after three failed attempts. Use Start to retry.";
            }
        }
        finally { _recovering = false; }
    }

    private void PolicyChanged(object sender, RoutedEventArgs e) { if (_isUiInitialized) { QueueSave(); ApplyPlaybackPolicy(); } }
    private void PolicyModeChanged(object sender, SelectionChangedEventArgs e) { if (_isUiInitialized) { QueueSave(); ApplyPlaybackPolicy(); } }
    private void FpsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized) return;
        _settings.FramesPerSecond = GetSelectedFps();
        _wallpaperController.SetFrameCap(_settings.FramesPerSecond);
        LivePreview.SetFrameCap(_settings.FramesPerSecond);
        QueueSave();
    }
    private int GetSelectedFps() => FpsComboBox.SelectedItem is ComboBoxItem item &&
        int.TryParse(item.Tag?.ToString(), out var fps) ? FrameRatePolicy.Normalize(fps) : 30;
    private PlaybackDecision Decision => PlaybackPolicy.Evaluate(AppPauseModeComboBox.SelectedIndex,
        _foregroundMonitor.IsFullscreenActive, _foregroundMonitor.IsOtherAppActive,
        PauseBatteryCheckBox.IsChecked == true, _foregroundMonitor.IsOnBattery,
        PauseScopeComboBox.SelectedIndex == 1, _foregroundMonitor.ForegroundMonitorIndex, _environment.SessionLocked);

    private void ApplyPlaybackPolicy()
    {
        if (!_isUiInitialized || _isQuitting) return;
        var decision = Decision;
        try
        {
            if (decision.PauseAll)
            {
                _wallpaperController.Pause();
                _wallpaperController.SetPausedMonitor(null);
            }
            else
            {
                _wallpaperController.SetPausedMonitor(decision.PausedMonitor);
                _wallpaperController.Resume();
            }
        }
        catch (Exception exception) { ReportError("Playback policy", exception); ScheduleRecovery(); }
        UpdatePreviewSuspension();
        UpdateStatus();
    }
    private void UpdatePreviewSuspension() => LivePreview.SetSuspended(!IsVisible ||
        WindowState == WindowState.Minimized || _environment.SessionLocked ||
        PauseBatteryCheckBox.IsChecked == true && _foregroundMonitor.IsOnBattery);
    private void UpdateStatus()
    {
        var state = _wallpaperController.IsRunning ? _wallpaperController.IsPaused ? "Paused" : "Running" : "Stopped";
        StatusText.Text = _wallpaperController.IsRunning ? $"{state} · {Decision.Reason}" : state;
        StatusText.ToolTip = _wallpaperController.ActiveRequest?.Id;
    }

    private void VisualizerSettingsButton_Click(object sender, RoutedEventArgs e) => VisualizerSettingsPopup.IsOpen = !VisualizerSettingsPopup.IsOpen;
    private void VisualizerSettingChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyVisualizerSettings();
    private void VisualizerColorChanged(object sender, SelectionChangedEventArgs e) => ApplyVisualizerSettings();
    private void ApplyVisualizerSettings()
    {
        if (!_isUiInitialized || Selected is not { IsVisualizer: true } entry) return;
        var preferences = new VisualizerPreferences((float)VisualizerIntensitySlider.Value,
            (float)VisualizerSensitivitySlider.Value, (float)VisualizerGlowSlider.Value, VisualizerColorComboBox.SelectedIndex).Normalize();
        _settings.Visualizers[entry.Id] = preferences;
        var settings = preferences.ToSettings();
        if (_wallpaperController.ActiveRequest?.Id == entry.Id) _wallpaperController.UpdateVisualizerSettings(settings);
        LivePreview.UpdateSettings(settings);
        QueueSave();
    }

    private async void AddVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a local video", CheckFileExists = true,
            Filter = "Video files|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.gif|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await MediaTools.ProbeAsync(dialog.FileName, _settings.MediaToolsDirectory, CancellationToken.None);
            MediaTools.Resolve("ffmpeg", _settings.MediaToolsDirectory);
            if (_isQuitting) return;
            var existing = _settings.Videos.FirstOrDefault(video => string.Equals(video.Path, dialog.FileName, StringComparison.OrdinalIgnoreCase));
            var video = existing ?? new LocalVideo("video:" + Guid.NewGuid().ToString("N"), dialog.FileName, Path.GetFileNameWithoutExtension(dialog.FileName));
            if (existing is null) _settings.Videos.Add(video);
            RefreshGallery();
            WallpaperGallery.SelectedItem = WallpaperGallery.Items.Cast<WallpaperEntry>().First(entry => entry.Id == video.Id);
            QueueSave();
            ErrorText.Text = "";
        }
        catch (Exception exception) { ReportError("Video could not be added", exception); }
    }
    private void MediaTools_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose ffmpeg.exe (ffprobe.exe must be in the same folder)", Filter = "FFmpeg|ffmpeg.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var folder = Path.GetDirectoryName(dialog.FileName)!;
        if (!File.Exists(Path.Combine(folder, "ffprobe.exe"))) { ErrorText.Text = "This folder also needs ffprobe.exe."; return; }
        _settings.MediaToolsDirectory = folder;
        QueueSave();
        UpdateSelection();
        ErrorText.Text = "";
    }
    private void OpenLibrary_Click(object sender, RoutedEventArgs e) => OpenFolder(_wallpaperLibrary.PackagesDirectory);
    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e) => OpenFolder(AppLog.LogDirectory);
    private void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) { ReportError("Folder could not be opened", exception); }
    }
    private void ReportError(string context, Exception exception)
    {
        AppLog.WriteException(context, exception);
        ErrorText.Text = $"{context}: {exception.Message}";
    }
    private void QueueSave()
    {
        if (!_isUiInitialized) return;
        _settings.AppPauseMode = AppPauseModeComboBox.SelectedIndex;
        _settings.PausePerMonitor = PauseScopeComboBox.SelectedIndex == 1;
        _settings.PauseOnBattery = PauseBatteryCheckBox.IsChecked == true;
        _settings.FramesPerSecond = GetSelectedFps();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void RememberDisplays()
    {
        var snapshots = DesktopWorker.GetMonitorTargets().Select(display => new SavedDisplay(
            display.DeviceId, display.DeviceName, display.X, display.Y, display.Width, display.Height,
            display.DpiX, display.DpiY));
        // Disconnected displays retain their snapshot; indices are never persisted as identity.
        _settings.Displays = (_settings.Displays ?? []).Concat(snapshots)
            .GroupBy(display => display.DeviceId).Select(group => group.Last()).ToList();
        QueueSave();
    }
    private void SavePreferences()
    {
        _saveTimer.Stop();
        if (!_settingsStore.Save(_settings)) ErrorText.Text = "Preferences could not be saved. Open diagnostics for details.";
    }

    private void InitializeTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "hypnix-tray-white.ico");
        try
        {
            _trayDrawingIcon = File.Exists(iconPath)
                ? new System.Drawing.Icon(iconPath)
                : Environment.ProcessPath is { } processPath
                    ? System.Drawing.Icon.ExtractAssociatedIcon(processPath)
                    : null;
        }
        catch (Exception exception) { AppLog.WriteException("Tray icon load failed; using default", exception); }
        // Guarantee a disposable, owned icon so the tray is always visible and cleanup is safe.
        _trayDrawingIcon ??= (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open HYPNIX", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add("Stop wallpaper", null, (_, _) => Dispatcher.Invoke(StopWallpaper));
        menu.Items.Add("Quit HYPNIX", null, (_, _) => Dispatcher.Invoke(() => { _isQuitting = true; Close(); }));
        _trayIcon = new System.Windows.Forms.NotifyIcon { Icon = _trayDrawingIcon, Text = "HYPNIX", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }
    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            SavePreferences();
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _trayIcon?.ShowBalloonTip(2500, "HYPNIX is still running", "Double-click the tray icon to reopen, or choose Quit HYPNIX to exit.", System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _isQuitting = true;
        SavePreferences();
        _recoveryTimer.Stop();
        _wallpaperLibrary.PackagesChanged -= OnPackagesChanged;
        _environment.Dispose();
        LivePreview.Dispose();
        _foregroundMonitor.Dispose();
        _wallpaperLibrary.Dispose();
        _wallpaperController.Dispose();
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.ContextMenuStrip?.Dispose(); _trayIcon.Dispose(); }
        _trayDrawingIcon?.Dispose();
        base.OnClosed(e);
    }
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(320, work.Width - 32));
        MinHeight = Math.Min(MinHeight, Math.Max(320, work.Height - 32));
        Width = Math.Min(1180, work.Width - 32);
        Height = Math.Min(760, work.Height - 32);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        UpdateSelection();
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
