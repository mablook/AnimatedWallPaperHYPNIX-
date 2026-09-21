using System.Windows;
using System.Windows.Controls;
using AnimatedWallPaper.Controls;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow
{
    private readonly MonitorPreviewSimulation _previewSimulation = new();
    private readonly Dictionary<string, string> _monitorPreviewDrafts = new(StringComparer.OrdinalIgnoreCase);
    private bool IsMultiPreview => PreviewModeCombo?.SelectedIndex > 0;
    private bool IsPreviewSimulation => PreviewModeCombo?.SelectedIndex >= 2;

    private void InitializeMonitorPreviews()
    {
        MultiPreview.DisplaySelected += id =>
        {
            if (IsPreviewSimulation) return;
            var display = TargetDisplayCombo.Items.Cast<PreviewDisplay>().FirstOrDefault(item => item.Target.DeviceId == id);
            if (display is not null) TargetDisplayCombo.SelectedItem = display;
        };
        MultiPreview.WallpaperSelected += (id, wallpaperId) =>
        {
            var catalog = WallpaperGallery.Items.Cast<WallpaperEntry>().ToArray();
            if (IsPreviewSimulation)
            {
                if (_previewSimulation.SelectWallpaper(id, wallpaperId, catalog)) UpdateMonitorPreviews();
                return;
            }
            var display = TargetDisplayCombo.Items.Cast<PreviewDisplay>().FirstOrDefault(item => item.Target.DeviceId == id);
            var entry = catalog.FirstOrDefault(item => item.Id == wallpaperId);
            if (display is null || entry is null) return;
            _monitorPreviewDrafts[id] = wallpaperId;
            TargetDisplayCombo.SelectedItem = display;
            WallpaperGallery.SelectedItem = entry;
            UpdateMonitorPreviews();
        };
        MultiPreview.SetAudioEnabled(_settings.AudioReactive);
        MultiPreview.AspectSelected += (id, aspect) =>
        {
            if (IsPreviewSimulation && _previewSimulation.SelectAspect(id, aspect)) UpdateMonitorPreviews();
        };
        MultiPreview.SetFrameCap(_settings.FramesPerSecond);
        MultiPreview.SetSuspended(true);
    }

    private void PreviewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiInitialized) return;
        _settingsWindow?.Close();
        _previewRequested = true;
        _settings.PreviewPaneCollapsed = false;
        if (!IsMultiPreview) _monitorPreviewDrafts.Clear();
        else if (!IsPreviewSimulation && _monitorPreviewDrafts.Count == 0 && SelectedDisplay is { } display && Selected is { } entry)
            _monitorPreviewDrafts[display.Target.DeviceId] = entry.Id;
        UpdateMonitorPreviews();
        UpdateLayoutMode();
        UpdateStatus();
        QueueSave();
    }

    private void UpdateMonitorPreviews()
    {
        if (!_isUiInitialized || _isQuitting || MultiPreview is null) return;
        SinglePreviewSurface.Visibility = IsMultiPreview ? Visibility.Collapsed : Visibility.Visible;
        FocusedDisplayControls.Visibility = IsMultiPreview ? Visibility.Collapsed : Visibility.Visible;
        MultiPreview.Visibility = IsMultiPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewStatusText.Visibility = IsMultiPreview ? Visibility.Collapsed : Visibility.Visible;
        PreviewHelpText.Text = IsPreviewSimulation
            ? "Simulation only · choose a wallpaper for each virtual display. Your desktop is unchanged."
            : "Preview only · your desktop changes when you apply";
        DisplayTargetBar.Visibility = SelectionFooter.Visibility = IsPreviewSimulation ? Visibility.Collapsed : Visibility.Visible;
        var catalog = WallpaperGallery.Items.Cast<WallpaperEntry>().ToArray();
        if (!IsMultiPreview)
        {
            MultiPreview.UpdateItems([], catalog);
            return;
        }

        var items = new List<MonitorPreviewItem>();
        if (IsPreviewSimulation)
        {
            _previewSimulation.SetCount(PreviewModeCombo.SelectedIndex == 2 ? 3 : 4, catalog);
            foreach (var display in _previewSimulation.Displays)
            {
                var entry = catalog.FirstOrDefault(item => item.Id == display.WallpaperId);
                items.Add(new(display.Id, display.Label, display.Aspect,
                    PreviewRequest(entry, null), false, true));
            }
        }
        else
        {
            var active = _wallpaperController.ActiveRequests;
            foreach (var display in TargetDisplayCombo.Items.Cast<PreviewDisplay>())
            {
                var deviceId = display.Target.DeviceId;
                var saved = _settings.DisplayWallpapers.GetValueOrDefault(deviceId);
                var id = _monitorPreviewDrafts.GetValueOrDefault(deviceId)
                    ?? active.GetValueOrDefault(deviceId)?.Id ?? (saved?.Enabled == true ? saved.WallpaperId : null);
                var entry = catalog.FirstOrDefault(item => item.Id == id);
                items.Add(new(deviceId, display.Label, display.Aspect,
                    PreviewRequest(entry, deviceId), Equals(display, SelectedDisplay), false));
            }
        }
        MultiPreview.UpdateItems(items, catalog);
    }

    private WallpaperRequest? PreviewRequest(WallpaperEntry? entry, string? displayId)
    {
        if (entry is null) return null;
        try { return RequestForDisplay(entry, displayId); }
        catch (Exception exception)
        {
            AppLog.WriteException($"Monitor preview unavailable: {entry.Id}", exception);
            return null;
        }
    }

    private void UpdatePreviewActions()
    {
        TargetDisplayCombo.IsEnabled = PreviewDisplayCombo.IsEnabled = !IsPreviewSimulation;
        VisualizerSettingsButton.IsEnabled = !IsPreviewSimulation;
        if (!IsPreviewSimulation) return;
        StartButton.IsEnabled = ApplyAllButton.IsEnabled = StopDisplayButton.IsEnabled = false;
    }
}
