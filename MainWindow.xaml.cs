using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

// CA1001: the disposable fields are owned for the whole app lifetime and released in OnClosed,
// which is the WPF window teardown hook; a Window is not expected to implement IDisposable.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001",
    Justification = "Owned services are disposed in OnClosed, the WPF window teardown path.")]
public partial class MainWindow : Window
{
    public static readonly DependencyProperty ActiveWallpaperIdProperty = DependencyProperty.Register(
        nameof(ActiveWallpaperId), typeof(string), typeof(MainWindow), new PropertyMetadata(null));
    public string? ActiveWallpaperId { get => (string?)GetValue(ActiveWallpaperIdProperty); private set => SetValue(ActiveWallpaperIdProperty, value); }
    private bool _previewRequested;
    private bool _appSettingsOpen;
    private sealed record PreviewDisplay(DesktopWorker.WallpaperTarget Target, int Number)
    {
        public string Label => $"Display {Number} · {Target.Width} × {Target.Height}";
        public double Aspect => (double)Target.Width / Math.Max(1, Target.Height);
    }
    private PreviewDisplay? SelectedDisplay => TargetDisplayCombo.SelectedItem as PreviewDisplay;
    private readonly DisplayWallpaperController _wallpaperController;
    private readonly Func<DesktopWorker.WallpaperTarget[]> _getMonitorTargets;
    private bool _syncingDisplaySelection;
    private readonly ForegroundAppMonitor _foregroundMonitor = new();
    private readonly WallpaperLibraryService _wallpaperLibrary;
    private readonly DesktopEnvironmentMonitor _environment = new();
    private readonly AppSettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ToolStripMenuItem? _trayAudioItem;
    private System.Windows.Forms.ToolStripMenuItem? _trayUpdateItem;
    private System.Drawing.Icon? _trayDrawingIcon;
    private UpdateService? _updateService;
    private Velopack.UpdateInfo? _pendingUpdate;
    private VisualizerSettingsWindow? _settingsWindow;
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

    internal MainWindow(AppSettingsStore settingsStore, string? libraryRoot, DisplayWallpaperController? controller = null, AppLicenseService? license = null,
        Func<DesktopWorker.WallpaperTarget[]>? getMonitorTargets = null)
    {
        _license = license ?? new AppLicenseService(LemonSqueezyLicenseProvider.Create());
        _wallpaperController = controller ?? new();
        _getMonitorTargets = getMonitorTargets ?? DesktopWorker.GetMonitorTargets;
        _settingsStore = settingsStore;
        _wallpaperLibrary = new WallpaperLibraryService(libraryRoot);
        _settings = _settingsStore.Load();
        InitializeComponent();
        InitializeMonitorPreviews();
        InitializeTrayIcon();
        AppPauseModeComboBox.SelectedIndex = _settings.AppPauseMode;
        PausePerMonitorToggle.IsChecked = _settings.PausePerMonitor;
        PauseBatteryCheckBox.IsChecked = _settings.PauseOnBattery;
        AudioReactiveCheckBox.IsChecked = _settings.AudioReactive;
        _wallpaperController.SetAudioEnabled(_settings.AudioReactive);
        LivePreview.SetAudioEnabled(_settings.AudioReactive);
        FpsComboBox.SelectedIndex = _settings.FramesPerSecond == 15 ? 0 : _settings.FramesPerSecond == 60 ? 2 : 1;
        UpdatePauseControlsEnabled();
        RefreshGallery();
        _isUiInitialized = true;
        UpdateSelection();
        _saveTimer.Tick += (_, _) => SavePreferences();
        _recoveryTimer.Tick += async (_, _) => { _recoveryTimer.Stop(); await RecoverAsync(); };
        _wallpaperLibrary.PackagesChanged += OnPackagesChanged;
        _foregroundMonitor.StateChanged += (_, _) => ApplyPlaybackPolicy();
        _wallpaperController.StateChanged += () => ApplyPlaybackPolicy();
        _environment.PolicyChanged += ApplyPlaybackPolicy;
        _environment.LayoutChanged += () => { _layoutRecoveryPending = true; RememberDisplays(); _recoveryAttempts = 0; ScheduleRecovery(); };
        _environment.HealthCheck += () =>
        {
            if (_wallpaperController.IsRunning && !_wallpaperController.IsHealthy) ScheduleRecovery();
        };
        LivePreview.StatusChanged += status => PreviewStatusText.Text = status;
        IsVisibleChanged += (_, _) => UpdatePreviewSuspension();
        StateChanged += (_, _) => UpdatePreviewSuspension();
        _foregroundMonitor.Start();
        RememberDisplays();
        _license.Changed += UpdateLicenseUi;
        _license.RefreshRequested += OnStoreLicenseChanged;
        Activated += RefreshLicenseOnActivate;
        UpdateLicenseUi();
        UpdateStatus();
        CheckForUpdates();
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
        LibraryStatusText.Text = invalid == 0 ? $"{entries.Count} wallpapers" : $"{entries.Count} wallpapers · {invalid} unavailable packages";
        _isUiInitialized = initialized;
        if (initialized) UpdateSelection();
    }

    private void WallpaperGallery_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized) return;
        if (IsMultiPreview && !IsPreviewSimulation && SelectedDisplay is { } display && Selected is { } entry)
            _monitorPreviewDrafts[display.Target.DeviceId] = entry.Id;
        UpdateSelection();
        QueueSave();
        UpdateStatus();
    }

    private void UpdateSelection()
    {
        if (Selected is not { } entry) { LivePreview.Select(null); return; }
        _settings.SelectedWallpaperId = entry.Id;
        ActivePreviewTitle.Text = entry.Title;
        ActivePreviewSubtitle.Text = entry.Description;
        VisualizerSettingsButton.Visibility = entry.IsVisualizer ? Visibility.Visible : Visibility.Collapsed;
        if (_settingsWindow is not null)
        {
            if (entry.IsVisualizer)
            {
                _settingsWindow.SetWallpaper(entry);
                _editorEntry = entry;
                _editorDisplayId = SelectedDisplay?.Target.DeviceId;
                _settingsWindow.LoadValues(CurrentPreferencesFor(entry));
            }
            else
            {
                _settingsWindow.Close();
            }
        }
        try {
            var request = RequestForDisplay(entry, SelectedDisplay?.Target.DeviceId);
            LivePreview.Select(request);
            _settingsWindow?.SetPreview(request, SelectedDisplay?.Aspect ?? 16d / 9, SelectedDisplay?.Label ?? "Preview", _settings.AudioReactive);
        }
        catch (Exception exception) { LivePreview.Select(null); ReportError("Preview", exception); }
        UpdateMonitorPreviews();
        UpdatePreviewSuspension();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsPreviewSimulation) return;
        if (!_license.CanPlay) { UpdateLicenseUi(); return; }
        _playRequested = true;
        await StartSelectedAsync();
    }

    private async Task StartSelectedAsync()
    {
        if (SelectedDisplay is { } display) await ApplyToDisplaysAsync([display.Target]);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopWallpaper();
    private void StopWallpaper() => StopWallpapers(persist: true);
    private void StopWallpapers(bool persist)
    {
        _playRequested = false;
        _selectionRevision++;
        _changingWallpaper = false;
        _recoveryTimer.Stop();
        _wallpaperController.Stop();
        if (persist)
        {
            foreach (var assignment in _settings.DisplayWallpapers.Values) assignment.Enabled = false;
            QueueSave();
        }
        StartButton.IsEnabled = true;
        UpdateStatus();
    }

    private void ScheduleRecovery()
    {
        if (_isQuitting || !_license.CanPlay || !_playRequested || _changingWallpaper || _recovering || _recoveryTimer.IsEnabled || _recoveryAttempts >= 3) return;
        _recoveryTimer.Start();
    }

    private async Task RecoverAsync()
    {
        if (_isQuitting || !_license.CanPlay || !_playRequested || _changingWallpaper || _recovering || _recoveryAttempts >= 3) return;
        _recovering = true;
        _layoutRecoveryPending = false;
        var revision = _selectionRevision;
        var retry = false;
        try
        {
            AppLog.Write("Rebuilding wallpaper after desktop/display/decoder change");
            var failures = new List<Exception>();
            try { await _wallpaperController.ReconcileAsync(_getMonitorTargets()); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { failures.Add(exception); }
            if (revision != _selectionRevision || !_playRequested || _isQuitting) return;
            if (!_license.CanPlay) { StopWallpapers(persist: false); return; }
            // A failed existing session must not prevent an independently saved display,
            // absent at startup, from receiving its own wallpaper when it reconnects.
            try { await RestoreMissingDisplaysAsync(); }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { failures.Add(exception); }
            if (revision != _selectionRevision || !_playRequested || _isQuitting) return;
            if (!_license.CanPlay) { StopWallpapers(persist: false); return; }
            if (failures.Count > 0) throw new AggregateException("Some displays could not be restored.", failures);
            _recoveryAttempts = 0;
            _foregroundMonitor.RefreshNow();
            ApplyPlaybackPolicy();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ReportError("Desktop recovery", exception);
            retry = true;
            if (++_recoveryAttempts >= 3)
            {
                ErrorText.Text += " Use Apply to retry the affected display. Other displays keep their wallpapers.";
            }
        }
        finally { _recovering = false; UpdateStatus(); if (_layoutRecoveryPending || retry && _recoveryAttempts < 3) ScheduleRecovery(); }
    }

    private void AudioReactiveChanged(object sender, RoutedEventArgs e)
    {
        if (!_isUiInitialized) return;
        var enabled = AudioReactiveCheckBox.IsChecked == true;
        _settings.AudioReactive = enabled;
        _wallpaperController.SetAudioEnabled(enabled);
        LivePreview.SetAudioEnabled(enabled);
        MultiPreview.SetAudioEnabled(enabled);
        _settingsWindow?.SetAudioEnabled(enabled);
        if (_trayAudioItem is not null && _trayAudioItem.Checked != enabled) _trayAudioItem.Checked = enabled;
        QueueSave();
    }
    private void PolicyChanged(object sender, RoutedEventArgs e) { if (_isUiInitialized) { QueueSave(); ApplyPlaybackPolicy(); } }
    private void PolicyModeChanged(object sender, SelectionChangedEventArgs e) { if (_isUiInitialized) { UpdatePauseControlsEnabled(); QueueSave(); ApplyPlaybackPolicy(); } }
    private void UpdatePauseControlsEnabled() => PausePerMonitorToggle.IsEnabled = AppPauseModeComboBox.SelectedIndex != 0;
    private void FpsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized) return;
        _settings.FramesPerSecond = GetSelectedFps();
        _wallpaperController.SetFrameCap(_settings.FramesPerSecond);
        LivePreview.SetFrameCap(_settings.FramesPerSecond);
        MultiPreview.SetFrameCap(_settings.FramesPerSecond);
        _settingsWindow?.SetFrameCap(_settings.FramesPerSecond);
        QueueSave();
    }
    private int GetSelectedFps() => FpsComboBox.SelectedItem is ComboBoxItem item &&
        int.TryParse(item.Tag?.ToString(), out var fps) ? FrameRatePolicy.Normalize(fps) : 30;
    private PlaybackDecision Decision => PlaybackPolicy.Evaluate(AppPauseModeComboBox.SelectedIndex,
        _foregroundMonitor.FullscreenMonitors, _foregroundMonitor.CoveredMonitors,
        PauseBatteryCheckBox.IsChecked == true, _foregroundMonitor.IsOnBattery,
        PausePerMonitorToggle.IsChecked == true, _environment.SessionLocked);

    private void ApplyPlaybackPolicy()
    {
        if (!_isUiInitialized || _isQuitting) return;
        var decision = Decision;
        try
        {
            if (decision.PauseAll)
            {
                _wallpaperController.Pause();
                _wallpaperController.SetPausedDisplays(Array.Empty<string>());
            }
            else
            {
                var displays = TargetDisplayCombo.Items.Cast<PreviewDisplay>().ToArray();
                _wallpaperController.SetPausedDisplays(decision.PausedMonitors.Where(index => index >= 0 && index < displays.Length)
                    .Select(index => displays[index].Target.DeviceId).ToArray());
                _wallpaperController.Resume();
            }
        }
        catch (Exception exception) { ReportError("Playback policy", exception); ScheduleRecovery(); }
        var pausedDisplays = TargetDisplayCombo.Items.Cast<PreviewDisplay>().Select((display, index) =>
            (display, index)).Where(value => _wallpaperController.IsDisplayPaused(value.display.Target.DeviceId))
            .Select(value => value.index).ToArray();
        var pauseReport = pausedDisplays.Length == 0 ? "none" : string.Join(",", pausedDisplays);
        if (_lastDisplayPauseReport != pauseReport)
        {
            _lastDisplayPauseReport = pauseReport;
            AppLog.Write($"Display wallpaper pause changed. Monitors={pauseReport}");
        }
        UpdatePreviewSuspension();
        UpdateStatus();
    }
    private void UpdatePreviewSuspension()
    {
        var suspended = !_license.CanPlay || !IsVisible || WindowState == WindowState.Minimized || _environment.SessionLocked ||
            PauseBatteryCheckBox.IsChecked == true && _foregroundMonitor.IsOnBattery;
        var editorVisible = _settingsWindow is { IsVisible: true, WindowState: not WindowState.Minimized };
        var previewSuspended = suspended || _appSettingsOpen || PreviewPanel.Visibility != Visibility.Visible || editorVisible;
        LivePreview.SetSuspended(previewSuspended || IsMultiPreview);
        MultiPreview.SetSuspended(previewSuspended || !IsMultiPreview);
        if (editorVisible) PreviewStatusText.Text = "Preview open in Customize";
        _settingsWindow?.SetPreviewSuspended(suspended);
    }
    private void UpdateStatus()
    {
        var state = _wallpaperController.IsRunning ? _wallpaperController.IsPaused ? "Paused" : "Running" : "Stopped";
        var active = SelectedDisplay is { } display ? _wallpaperController.ActiveRequests.GetValueOrDefault(display.Target.DeviceId) : null;
        ActiveWallpaperId = active?.Id;
        var title = WallpaperGallery.Items.Cast<WallpaperEntry>().FirstOrDefault(entry => entry.Id == ActiveWallpaperId)?.Title;
        var count = _wallpaperController.ActiveRequests.Count;
        StatusText.Text = _changingWallpaper ? "Preparing wallpaper…" : count == 0 ? state : $"{state} · {count} {(count == 1 ? "display" : "displays")}";
        DisplayWallpaperText.Text = title is null ? "No wallpaper on this display" : $"{(_wallpaperController.IsDisplayPaused(SelectedDisplay!.Target.DeviceId) ? "Paused" : "Playing")} · {title}";
        StatusText.ToolTip = Decision.Reason;
        StopButton.IsEnabled = _wallpaperController.IsRunning || _changingWallpaper || _restoringDisplays || _recovering || _playRequested;
        StartButton.IsEnabled = _license.CanPlay && Selected is not null && SelectedDisplay is not null && !_changingWallpaper && !_recovering && !_restoringDisplays;
        ApplyAllButton.IsEnabled = StartButton.IsEnabled;
        ApplyAllButton.Visibility = TargetDisplayCombo.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        StopDisplayButton.IsEnabled = active is not null && !_changingWallpaper && !_recovering && !_restoringDisplays;
        StartButton.Content = SelectedDisplay is { } selectedDisplay ? $"Apply to display {selectedDisplay.Number}" : "Apply wallpaper";
        ApplyTargetText.Text = SelectedDisplay is { } target ? $"Only display {target.Number} will change" : "Connect a display to apply";
        UpdatePreviewActions();
        UpdateMonitorPreviews();
    }

    private void VisualizerSettingsButton_Click(object sender, RoutedEventArgs e) => OpenVisualizerSettingsWindow();

    private void OpenVisualizerSettingsWindow()
    {
        if (IsPreviewSimulation || !_license.CanPlay || Selected is not { IsVisualizer: true } entry) return;
        if (_settingsWindow is null)
        {
            _settingsWindow = new VisualizerSettingsWindow { Owner = this };
            _settingsWindow.SetPresetLibrary(_settings.VisualizerPresets);
            _settingsWindow.PresetsChanged += QueueSave;
            _settingsWindow.Changed += OnVisualizerWindowChanged;
            _settingsWindow.StateChanged += (_, _) => UpdatePreviewSuspension();
            _settingsWindow.IsVisibleChanged += (_, _) => UpdatePreviewSuspension();
            _settingsWindow.Closed += (_, _) => { _settingsWindow = null; _editorDisplayId = null; _editorEntry = null; UpdatePreviewSuspension(); };
        }
        _settingsWindow.SetWallpaper(entry);
        _editorDisplayId = SelectedDisplay?.Target.DeviceId;
        _editorEntry = entry;
        _settingsWindow.LoadValues(CurrentPreferencesFor(entry));
        _settingsWindow.SetPreview(RequestForDisplay(entry, SelectedDisplay?.Target.DeviceId), SelectedDisplay?.Aspect ?? 16d / 9,
            SelectedDisplay?.Label ?? "Preview", _settings.AudioReactive);
        UpdatePreviewSuspension();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private VisualizerPreferences CurrentPreferencesFor(WallpaperEntry entry)
        => PreferencesForDisplay(entry, SelectedDisplay?.Target.DeviceId);

    private void OnVisualizerWindowChanged(VisualizerPreferences preferences)
    {
        if (_editorEntry is { } entry && _editorDisplayId is { } deviceId)
            ApplyPreferencesForDisplay(entry, deviceId, preferences);
    }

    // Applies the selected wallpaper's stored preferences to the live session and preview
    // (used when a wallpaper starts). Live edits come through ApplyVisualizerPreferences.
    private void ApplyVisualizerSettings()
    {
        if (Selected is not { IsVisualizer: true } entry) return;
        ApplyVisualizerPreferences(CurrentPreferencesFor(entry));
    }

    private void ApplyVisualizerPreferences(VisualizerPreferences preferences)
    {
        if (Selected is not { IsVisualizer: true } entry) return;
        if (SelectedDisplay is { } display) ApplyPreferencesForDisplay(entry, display.Target.DeviceId, preferences);
    }
    private void ApplyPreferencesForDisplay(WallpaperEntry entry, string deviceId, VisualizerPreferences preferences)
    {
        var normalized = preferences.Normalize();
        DisplayPreferences(deviceId).Visualizers[entry.Id] = normalized;
        var settings = normalized.ToSettings();
        if (_wallpaperController.ActiveRequests.GetValueOrDefault(deviceId)?.Id == entry.Id)
            _wallpaperController.UpdateVisualizerSettings(deviceId, settings);
        if (SelectedDisplay?.Target.DeviceId == deviceId && Selected?.Id == entry.Id) LivePreview.UpdateSettings(settings);
        UpdateMonitorPreviews();
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
        try { Directory.CreateDirectory(path); using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
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
        _settings.PausePerMonitor = PausePerMonitorToggle.IsChecked == true;
        _settings.PauseOnBattery = PauseBatteryCheckBox.IsChecked == true;
        _settings.AudioReactive = AudioReactiveCheckBox.IsChecked == true;
        _settings.FramesPerSecond = GetSelectedFps();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void RememberDisplays()
    {
        var targets = _getMonitorTargets();
        var selectedDevice = SelectedDisplay?.Target.DeviceId;
        var displays = targets.Select((display, index) => new PreviewDisplay(display, index + 1)).ToList();
        _syncingDisplaySelection = true;
        TargetDisplayCombo.ItemsSource = PreviewDisplayCombo.ItemsSource = displays;
        TargetDisplayCombo.SelectedItem = PreviewDisplayCombo.SelectedItem = displays.FirstOrDefault(display =>
            string.Equals(display.Target.DeviceId, selectedDevice, StringComparison.OrdinalIgnoreCase)) ?? displays.FirstOrDefault();
        _syncingDisplaySelection = false;
        UpdateDisplaySelection();
        var snapshots = targets.Select(display => new SavedDisplay(
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
        // Shown only once an update has been downloaded and is ready to apply on restart.
        _trayUpdateItem = new System.Windows.Forms.ToolStripMenuItem("Restart to update HYPNIX") { Visible = false };
        _trayUpdateItem.Click += (_, _) => Dispatcher.Invoke(ApplyUpdate);
        menu.Items.Add(_trayUpdateItem);
        menu.Items.Add("Open HYPNIX", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add("Stop all wallpapers", null, (_, _) => Dispatcher.Invoke(StopWallpaper));
        _trayAudioItem = new System.Windows.Forms.ToolStripMenuItem("Audio reactive") { Checked = _settings.AudioReactive, CheckOnClick = true };
        _trayAudioItem.CheckedChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if ((AudioReactiveCheckBox.IsChecked == true) != _trayAudioItem!.Checked) AudioReactiveCheckBox.IsChecked = _trayAudioItem.Checked;
        });
        menu.Items.Add(_trayAudioItem);
        menu.Items.Add("Quit HYPNIX", null, (_, _) => Dispatcher.Invoke(() => { _isQuitting = true; Close(); }));
        _trayIcon = new System.Windows.Forms.NotifyIcon { Icon = _trayDrawingIcon, Text = "HYPNIX", ContextMenuStrip = menu, Visible = true };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }
    // Also invoked by App when a second launch asks the running instance to surface from the tray.
    internal void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        // A brief topmost toggle reliably raises the window even when another app holds the
        // foreground (e.g. the just-launched second instance), then restores normal Z-order.
        Topmost = true;
        Topmost = false;
        Activate();
    }

    // Background update check on startup. No-op unless installed via Velopack; a failed/unreachable
    // check is logged and ignored so it never disrupts a running wallpaper. When an update is ready
    // it is downloaded and surfaced in the tray, applied only when the user chooses to restart.
    private async void CheckForUpdates()
    {
        try
        {
            _updateService = new UpdateService();
            if (!_updateService.IsInstalled) return;
            var update = await _updateService.CheckAndDownloadAsync();
            if (update is null || _isQuitting || _trayUpdateItem is null) return;
            _pendingUpdate = update;
            _trayUpdateItem.Text = $"Restart to update HYPNIX to {update.TargetFullRelease.Version}";
            _trayUpdateItem.Visible = true;
            _trayIcon?.ShowBalloonTip(4000, "HYPNIX update ready",
                "A new version was downloaded. Choose \u201CRestart to update HYPNIX\u201D in the tray menu.",
                System.Windows.Forms.ToolTipIcon.Info);
            AppLog.Write($"Update downloaded and ready: {update.TargetFullRelease.Version}");
        }
        catch (Exception exception) { AppLog.WriteException("Update check failed", exception); }
    }

    private void ApplyUpdate()
    {
        if (_pendingUpdate is null || _updateService is null) return;
        AppLog.Write("Applying update and restarting.");
        _isQuitting = true;
        _licenseTimer.Stop();
        _license.Changed -= UpdateLicenseUi;
        _license.RefreshRequested -= OnStoreLicenseChanged;
        _license.Dispose();
        SavePreferences();
        _settingsWindow?.Close();
        _updateService.ApplyAndRestart(_pendingUpdate);
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isQuitting)
        {
            e.Cancel = true;
            SavePreferences();
            _settingsWindow?.Close();
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
        _licenseTimer.Stop();
        _license.Changed -= UpdateLicenseUi;
        _license.RefreshRequested -= OnStoreLicenseChanged;
        _license.Dispose();
        SavePreferences();
        _settingsWindow?.Close();
        _recoveryTimer.Stop();
        _wallpaperLibrary.PackagesChanged -= OnPackagesChanged;
        _environment.Dispose();
        LivePreview.Dispose();
        MultiPreview.Dispose();
        _foregroundMonitor.Dispose();
        _wallpaperLibrary.Dispose();
        _wallpaperController.Dispose();
        if (_trayIcon is not null) { _trayIcon.Visible = false; _trayIcon.ContextMenuStrip?.Dispose(); _trayIcon.Dispose(); }
        _trayDrawingIcon?.Dispose();
        base.OnClosed(e);
    }
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var enabled = 1;
        // DWMWA_USE_IMMERSIVE_DARK_MODE (20). Best-effort: unsupported on older Windows builds,
        // so a non-zero HRESULT just means the title bar stays light; log it rather than ignore it.
        var darkTitleBar = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        if (darkTitleBar != 0) AppLog.Write($"Dark title bar unavailable (HRESULT=0x{darkTitleBar:X8})");
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, Math.Max(320, work.Width - 32));
        MinHeight = Math.Min(MinHeight, Math.Max(320, work.Height - 32));
        Width = Math.Min(1280, work.Width - 32);
        Height = Math.Min(820, work.Height - 32);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
        UpdateSelection();
        if (Selected is { } selected) WallpaperGallery.ScrollIntoView(selected);
        await InitializeLicenseAsync();
        if (_license.CanPlay)
        {
            try { await RestoreMissingDisplaysAsync(); }
            catch (Exception exception) { ReportError("Restore wallpapers", exception); ScheduleRecovery(); }
        }
    }

    private void ShellRoot_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();
    private void UpdateLayoutMode()
    {
        if (!_isUiInitialized) return;
        var mode = LibraryLayout.Resolve(ShellRoot.ActualWidth, ShellRoot.ActualHeight, _previewRequested, _settings.PreviewPaneCollapsed);
        LicenseBanner.Visibility = !_appSettingsOpen && _license.Snapshot.Kind is AppLicenseKind.Trial or AppLicenseKind.Checking
            ? Visibility.Visible : Visibility.Collapsed;
        if (IsMultiPreview && _previewRequested) mode = LibraryPreviewMode.Preview;
        LibraryWorkspace.Visibility = _appSettingsOpen || LicenseGateVisible ? Visibility.Collapsed : Visibility.Visible;
        AppSettingsPage.Visibility = _appSettingsOpen && !LicenseGateVisible ? Visibility.Visible : Visibility.Collapsed;
        LicenseGatePage.Visibility = LicenseGateVisible ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility = mode == LibraryPreviewMode.Preview ? Visibility.Collapsed : Visibility.Visible;
        PreviewPanel.Visibility = mode == LibraryPreviewMode.Library ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(PreviewPanel, mode == LibraryPreviewMode.Docked ? 2 : 0);
        Grid.SetColumnSpan(PreviewPanel, mode == LibraryPreviewMode.Docked ? 1 : 3);
        PreviewColumn.Width = mode == LibraryPreviewMode.Docked ? new GridLength(Math.Min(420, ShellRoot.ActualWidth * .35)) : new GridLength(0);
        PreviewGap.Width = new GridLength(mode == LibraryPreviewMode.Docked ? 24 : 0);
        PreviewPanel.Padding = mode == LibraryPreviewMode.Docked ? new Thickness(24, 0, 0, 0) : new Thickness(0);
        PreviewPanel.BorderThickness = mode == LibraryPreviewMode.Docked ? new Thickness(1, 0, 0, 0) : new Thickness(0);
        ClosePreviewButton.Content = mode == LibraryPreviewMode.Preview ? "Back to library" : "Hide preview";
        PreviewButton.Visibility = mode == LibraryPreviewMode.Library ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreviewSuspension();
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        _previewRequested = true; _settings.PreviewPaneCollapsed = false; QueueSave(); UpdateLayoutMode();
    }
    private void ClosePreview_Click(object sender, RoutedEventArgs e)
    {
        if (IsMultiPreview) PreviewModeCombo.SelectedIndex = 0;
        if (LibraryLayout.Resolve(ShellRoot.ActualWidth, ShellRoot.ActualHeight, _previewRequested, _settings.PreviewPaneCollapsed) == LibraryPreviewMode.Docked)
            _settings.PreviewPaneCollapsed = true;
        _previewRequested = false; QueueSave(); UpdateLayoutMode();
    }
    private void AppSettings_Click(object sender, RoutedEventArgs e) { _appSettingsOpen = true; UpdateLayoutMode(); }
    private void BackToLibrary_Click(object sender, RoutedEventArgs e) { _appSettingsOpen = false; if (IsMultiPreview) PreviewModeCombo.SelectedIndex = 0; _previewRequested = false; UpdateLayoutMode(); }
    private void PreviewDisplay_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized || _syncingDisplaySelection) return;
        _syncingDisplaySelection = true;
        if (ReferenceEquals(sender, PreviewDisplayCombo)) TargetDisplayCombo.SelectedItem = PreviewDisplayCombo.SelectedItem;
        else PreviewDisplayCombo.SelectedItem = TargetDisplayCombo.SelectedItem;
        _syncingDisplaySelection = false;
        UpdateDisplaySelection();
    }
    private void MonitorMap_SizeChanged(object sender, SizeChangedEventArgs e) => DrawMonitorMap();
    private void DrawMonitorMap()
    {
        if (MonitorMap is null) return;
        MonitorMap.Children.Clear();
        var displays = PreviewDisplayCombo.Items.Cast<PreviewDisplay>().ToArray();
        if (displays.Length == 0 || MonitorMap.ActualWidth <= 0) return;
        var left = displays.Min(d => d.Target.X); var top = displays.Min(d => d.Target.Y);
        var width = displays.Max(d => d.Target.X + d.Target.Width) - left;
        var height = displays.Max(d => d.Target.Y + d.Target.Height) - top;
        var scale = Math.Min((MonitorMap.ActualWidth - 12) / Math.Max(1, width), 60d / Math.Max(1, height));
        foreach (var display in displays)
        {
            var button = new System.Windows.Controls.Button {
                Content = display.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), ToolTip = display.Label,
                Width = Math.Max(1, display.Target.Width * scale - 4), Height = Math.Max(1, display.Target.Height * scale - 4),
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0),
                BorderBrush = (Brush)FindResource(Equals(display, SelectedDisplay) ? "AccentBrush" : "InputBorderBrush"),
                BorderThickness = new Thickness(Equals(display, SelectedDisplay) ? 2 : 1)
            };
            System.Windows.Automation.AutomationProperties.SetName(button, $"Preview {display.Label}");
            button.Click += (_, _) => TargetDisplayCombo.SelectedItem = display;
            Canvas.SetLeft(button, (MonitorMap.ActualWidth - width * scale) / 2 + (display.Target.X - left) * scale);
            Canvas.SetTop(button, 6 + (display.Target.Y - top) * scale);
            MonitorMap.Children.Add(button);
        }
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
