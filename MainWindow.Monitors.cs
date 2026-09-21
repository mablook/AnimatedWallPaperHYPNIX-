using System.Windows;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow
{
    private bool _restoringDisplays;
    private bool _layoutRecoveryPending;
    private string? _editorDisplayId;
    private WallpaperEntry? _editorEntry;
    private string? _lastDisplayPauseReport;

    private DisplayWallpaperSettings DisplayPreferences(string deviceId)
    {
        if (!_settings.DisplayWallpapers.TryGetValue(deviceId, out var value))
            _settings.DisplayWallpapers[deviceId] = value = new();
        return value;
    }

    private VisualizerPreferences PreferencesForDisplay(WallpaperEntry entry, string? deviceId)
        => (deviceId is not null && _settings.DisplayWallpapers.TryGetValue(deviceId, out var display)
                ? display.Visualizers.GetValueOrDefault(entry.Id) : null)
            ?? _settings.Visualizers.GetValueOrDefault(entry.Id) ?? entry.Defaults ?? new();

    private WallpaperRequest RequestForDisplay(WallpaperEntry entry, string? deviceId)
        => WallpaperCatalog.Request(entry, _settings) with { Settings = PreferencesForDisplay(entry, deviceId).ToSettings() };

    private void UpdateDisplaySelection()
    {
        if (!_isUiInitialized || PreviewAspect is null) return;
        // Editor callbacks retain their original display until the editor is closed.
        _settingsWindow?.Close();
        PreviewAspect.AspectRatio = SelectedDisplay?.Aspect ?? 16d / 9;
        if (SelectedDisplay is { } display)
        {
            var id = (IsMultiPreview ? _monitorPreviewDrafts.GetValueOrDefault(display.Target.DeviceId) : null)
                ?? _wallpaperController.ActiveRequests.GetValueOrDefault(display.Target.DeviceId)?.Id
                ?? _settings.DisplayWallpapers.GetValueOrDefault(display.Target.DeviceId)?.WallpaperId;
            if (id is not null && WallpaperGallery.Items.Cast<WallpaperEntry>().FirstOrDefault(entry => entry.Id == id) is { } entry)
                WallpaperGallery.SelectedItem = entry;
        }
        UpdateSelection(); UpdateStatus(); DrawMonitorMap();
    }

    private async void ApplyAll_Click(object sender, RoutedEventArgs e)
        => await ApplyToDisplaysAsync(_getMonitorTargets(), copySelectedPreferences: true);

    private async Task ApplyToDisplaysAsync(IReadOnlyList<DesktopWorker.WallpaperTarget> targets, bool copySelectedPreferences = false)
    {
        if (IsPreviewSimulation || !_license.CanPlay || _isQuitting || _changingWallpaper || _recovering || _restoringDisplays || Selected is not { } entry) return;
        var preferences = CurrentPreferencesFor(entry);
        var revision = ++_selectionRevision;
        _changingWallpaper = true;
        _playRequested = true;
        _recoveryAttempts = 0;
        _recoveryTimer.Stop();
        ErrorText.Text = "";
        UpdateStatus();
        var errors = new List<string>();
        try
        {
            foreach (var target in targets)
            {
                if (revision != _selectionRevision || !_license.CanPlay || _isQuitting) break;
                try
                {
                    var request = RequestForDisplay(entry, target.DeviceId);
                    if (copySelectedPreferences) request = request with { Settings = preferences.ToSettings() };
                    await _wallpaperController.StartAsync(request, target);
                    if (revision != _selectionRevision || _isQuitting) break;
                    if (!_license.CanPlay) { StopWallpapers(persist: false); break; }
                    var assignment = DisplayPreferences(target.DeviceId);
                    assignment.WallpaperId = entry.Id;
                    assignment.Enabled = true;
                    _monitorPreviewDrafts[target.DeviceId] = entry.Id;
                    if (copySelectedPreferences) assignment.Visualizers[entry.Id] = preferences;
                    QueueSave();
                }
                catch (OperationCanceledException) { }
                catch (Exception exception)
                {
                    AppLog.WriteException($"Apply wallpaper to {target.DeviceName}", exception);
                    var number = Array.FindIndex(_getMonitorTargets(), candidate => candidate.DeviceId == target.DeviceId) + 1;
                    errors.Add($"Display {number}: {exception.Message}");
                }
            }
            if (revision == _selectionRevision && errors.Count > 0) ErrorText.Text = string.Join("\n", errors);
        }
        finally
        {
            if (revision == _selectionRevision)
            {
                _changingWallpaper = false;
                _playRequested = _wallpaperController.DesiredRequests.Count > 0 || _settings.DisplayWallpapers.Values.Any(value => value.Enabled);
                _foregroundMonitor.RefreshNow();
                ApplyPlaybackPolicy();
                UpdateSelection();
                UpdateStatus();
                if (_layoutRecoveryPending) ScheduleRecovery();
            }
        }
    }

    private void StopDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (IsPreviewSimulation || _changingWallpaper || _recovering || SelectedDisplay is not { } display) return;
        _wallpaperController.Stop(display.Target.DeviceId);
        DisplayPreferences(display.Target.DeviceId).Enabled = false;
        _playRequested = _wallpaperController.DesiredRequests.Count > 0 || _settings.DisplayWallpapers.Values.Any(value => value.Enabled);
        QueueSave(); UpdateStatus();
    }

    private async Task RestoreMissingDisplaysAsync()
    {
        if (_restoringDisplays || !_license.CanPlay || _isQuitting) return;
        _restoringDisplays = true;
        var revision = _selectionRevision;
        var errors = new List<Exception>();
        try
        {
            UpdateStatus();
            foreach (var target in _getMonitorTargets())
            {
                if (revision != _selectionRevision || !_license.CanPlay || _isQuitting) break;
                if (_wallpaperController.ActiveRequests.ContainsKey(target.DeviceId) ||
                    _settings.DisplayWallpapers.GetValueOrDefault(target.DeviceId) is not { Enabled: true } saved) continue;
                var entry = WallpaperGallery.Items.Cast<WallpaperEntry>().FirstOrDefault(candidate => candidate.Id == saved.WallpaperId);
                if (entry is null) { ErrorText.Text = "A saved wallpaper is unavailable. Choose another wallpaper for that display."; continue; }
                try { await _wallpaperController.StartAsync(RequestForDisplay(entry, target.DeviceId), target); }
                catch (OperationCanceledException) { }
                catch (Exception exception) { errors.Add(exception); }
            }
            _playRequested = _wallpaperController.DesiredRequests.Count > 0 || _settings.DisplayWallpapers.Values.Any(value => value.Enabled);
            if (!_license.CanPlay || _isQuitting) StopWallpapers(persist: false);
            if (errors.Count > 0) throw new AggregateException("Some displays could not be restored", errors);
        }
        finally { _restoringDisplays = false; ApplyPlaybackPolicy(); UpdateStatus(); }
    }
}
