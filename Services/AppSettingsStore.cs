using System.IO;
using System.Text.Json;

namespace AnimatedWallPaper.Services;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int VisualizerDefaultsVersion { get; set; }
    // Bumped when the audio-reaction preferences shape changes; drives one-time migration on load.
    public int AudioSettingsVersion { get; set; }
    public string SelectedWallpaperId { get; set; } = "built-in-ambient";
    public int FramesPerSecond { get; set; } = 30;
    public int AppPauseMode { get; set; } = 2;
    public bool PausePerMonitor { get; set; }
    public bool PauseOnBattery { get; set; }
    public bool AudioReactive { get; set; } = true;
    // Legacy flag retained for backward/forward compatibility and migration. AudioSource is the
    // authority; this is kept in sync (mic included => true) so a downgrade still behaves sensibly.
    public bool MicrophoneReactive { get; set; }
    // Which reactive input(s) to analyze while AudioReactive is on. New profiles default to system.
    public AudioReactionSource AudioSource { get; set; } = AudioReactionSource.System;
    // Explicit endpoint selections by stable MMDevice id; null means follow the Windows default.
    public string? MicrophoneDeviceId { get; set; }
    public string? SystemAudioDeviceId { get; set; }
    public bool PreviewPaneCollapsed { get; set; }
    // Start HYPNIX automatically when the user signs in to Windows. The actual registration lives in
    // the OS (MSIX StartupTask or the per-user Run key); this mirrors the user's choice and the last
    // known OS state, reconciled on launch in case Windows/Task Manager changed it out of band.
    public bool StartWithWindows { get; set; }
    public string? MediaToolsDirectory { get; set; }
    public Dictionary<string, VisualizerPreferences> Visualizers { get; set; } = [];
    public Dictionary<string, DisplayWallpaperSettings> DisplayWallpapers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<VisualizerPreset> VisualizerPresets { get; set; } = [];
    public List<LocalVideo> Videos { get; set; } = [];
    public List<SavedDisplay> Displays { get; set; } = [];
}

internal sealed class DisplayWallpaperSettings
{
    public string WallpaperId { get; set; } = "built-in-ambient";
    public bool Enabled { get; set; }
    public Dictionary<string, VisualizerPreferences> Visualizers { get; set; } = [];
}

internal sealed record LocalVideo(string Id, string Path, string Title);
internal sealed record SavedDisplay(string DeviceId, string DeviceName, int X, int Y,
    int Width, int Height, uint DpiX, uint DpiY);
internal sealed record VisualizerPreferences(float Intensity = 3f, float Sensitivity = 4f,
    float Glow = 1.2f, int ColorTheme = 0, float Scale = 1f, float OffsetX = 0f, float OffsetY = 0f,
    VisualizerBackground? Background = null, bool Sparks = true)
{
    public VisualizerPreferences Normalize() => new(
        float.IsFinite(Intensity) ? Math.Clamp(Intensity, 0, 8) : 3,
        float.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, 0, 12) : 4,
        float.IsFinite(Glow) ? Math.Clamp(Glow, 0, 3) : 1.2f,
        Math.Clamp(ColorTheme, 0, 3),
        float.IsFinite(Scale) ? Math.Clamp(Scale, 0.3f, 3f) : 1f,
        float.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -1f, 1f) : 0f,
        float.IsFinite(OffsetY) ? Math.Clamp(OffsetY, -1f, 1f) : 0f,
        Background?.Normalize(), Sparks);

    public VisualizerSettings ToSettings()
    {
        var value = Normalize();
        var (start, end) = value.ColorTheme switch
        {
            1 => (System.Drawing.Color.FromArgb(255, 82, 120), System.Drawing.Color.FromArgb(255, 185, 70)),
            2 => (System.Drawing.Color.FromArgb(70, 255, 185), System.Drawing.Color.FromArgb(20, 160, 120)),
            3 => (System.Drawing.Color.FromArgb(245, 245, 250), System.Drawing.Color.FromArgb(120, 130, 150)),
            _ => (System.Drawing.Color.FromArgb(88, 205, 255), System.Drawing.Color.FromArgb(104, 80, 255))
        };
        return new(value.Intensity, value.Sensitivity, value.Glow, start, end, value.Scale, value.OffsetX, value.OffsetY, value.Background, value.Sparks);
    }
}

internal sealed class AppSettingsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Persist AudioSource as a readable name ("System"/"Microphone"/"SystemAndMicrophone");
        // integer values remain accepted so a hand-edited or older file still loads.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: true) }
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new();
            if (new FileInfo(_filePath).Length > 1024 * 1024) return new();
            var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), _jsonOptions);
            if (value is null || value.SchemaVersion != 1) return new();
            value.FramesPerSecond = FrameRatePolicy.Normalize(value.FramesPerSecond);
            value.AppPauseMode = Math.Clamp(value.AppPauseMode, 0, 2);
            if (string.IsNullOrWhiteSpace(value.SelectedWallpaperId))
                value.SelectedWallpaperId = "built-in-ambient";
            value.AudioSource = value.AudioSource.Normalize();
            value.MicrophoneDeviceId = Blank(value.MicrophoneDeviceId);
            value.SystemAudioDeviceId = Blank(value.SystemAudioDeviceId);
            if (value.AudioSettingsVersion < 1)
            {
                // Migrate the old two-flag model without changing effective behavior or discarding an
                // explicit choice: a legacy file (no AudioSource) with "React to microphone" on maps
                // to system + microphone; everything else stays system. AudioReactive (master) is
                // untouched, so master off stays off. New profiles already default to system audio.
                if (value.AudioSource == AudioReactionSource.System && value.MicrophoneReactive)
                    value.AudioSource = AudioReactionSource.SystemAndMicrophone;
                value.AudioSettingsVersion = 1;
            }
            // Keep the legacy flag consistent with the authoritative source for downgrade safety.
            value.MicrophoneReactive = value.AudioSource.UsesMicrophone();
            value.Visualizers = NormalizeVisualizers(value.Visualizers);
            var displayWallpapers = new Dictionary<string, DisplayWallpaperSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var (deviceId, displaySettings) in value.DisplayWallpapers ?? [])
            {
                if (string.IsNullOrWhiteSpace(deviceId) || displaySettings is null) continue;
                if (string.IsNullOrWhiteSpace(displaySettings.WallpaperId))
                    displaySettings.WallpaperId = "built-in-ambient";
                displaySettings.Visualizers = NormalizeVisualizers(displaySettings.Visualizers);
                // Device IDs survive reconnects and may differ only in casing. Keep saved
                // assignments even when that display is absent from the current desktop.
                displayWallpapers[deviceId] = displaySettings;
            }
            value.DisplayWallpapers = displayWallpapers;
            value.VisualizerPresets = VisualizerPresetLibrary.Normalize(value.VisualizerPresets);
            if (value.VisualizerDefaultsVersion < 2)
            {
                // Migrate only untouched built-in defaults, once. Retain chosen colors,
                // custom settings and imported packages' authored parameters.
                foreach (var (id, preferences) in value.Visualizers.ToArray())
                {
                    if (!id.StartsWith("package:", StringComparison.Ordinal) &&
                        ((value.VisualizerDefaultsVersion < 1 && preferences is { Intensity: 1, Sensitivity: 1, Glow: 0.55f }) ||
                         preferences is { Intensity: 2.1f, Sensitivity: 2.3f, Glow: 0.8f }))
                        value.Visualizers[id] = new(ColorTheme: preferences.ColorTheme);
                }
                value.VisualizerDefaultsVersion = 2;
            }
            value.Videos = (value.Videos ?? []).Where(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Path) &&
                Path.IsPathFullyQualified(item.Path)).DistinctBy(item => item.Id).ToList();
            value.Displays = (value.Displays ?? []).Where(item => item is not null &&
                !string.IsNullOrWhiteSpace(item.DeviceId)).ToList();
            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.WriteException("Preferences could not be read; using defaults", exception);
            return new();
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, VisualizerPreferences> NormalizeVisualizers(
        Dictionary<string, VisualizerPreferences>? visualizers) => (visualizers ?? [])
        .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value is not null)
        .ToDictionary(item => item.Key, item => item.Value.Normalize(), StringComparer.Ordinal);

    public bool Save(AppSettings settings)
    {
        var temporary = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, _jsonOptions));
            File.Move(temporary, _filePath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.WriteException("Preferences could not be saved", exception);
            return false;
        }
    }
}
