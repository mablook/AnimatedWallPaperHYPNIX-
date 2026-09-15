using System.IO;
using System.Text.Json;

namespace AnimatedWallPaper.Services;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int VisualizerDefaultsVersion { get; set; }
    public string SelectedWallpaperId { get; set; } = "built-in-ambient";
    public int FramesPerSecond { get; set; } = 30;
    public int AppPauseMode { get; set; } = 2;
    public bool PausePerMonitor { get; set; }
    public bool PauseOnBattery { get; set; }
    public bool AudioReactive { get; set; } = true;
    public string? MediaToolsDirectory { get; set; }
    public Dictionary<string, VisualizerPreferences> Visualizers { get; set; } = [];
    public List<LocalVideo> Videos { get; set; } = [];
    public List<SavedDisplay> Displays { get; set; } = [];
}

internal sealed record LocalVideo(string Id, string Path, string Title);
internal sealed record SavedDisplay(string DeviceId, string DeviceName, int X, int Y,
    int Width, int Height, uint DpiX, uint DpiY);
internal sealed record VisualizerPreferences(float Intensity = 3f, float Sensitivity = 4f,
    float Glow = 1.2f, int ColorTheme = 0)
{
    public VisualizerPreferences Normalize() => new(
        float.IsFinite(Intensity) ? Math.Clamp(Intensity, 0, 8) : 3,
        float.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, 0, 12) : 4,
        float.IsFinite(Glow) ? Math.Clamp(Glow, 0, 3) : 1.2f,
        Math.Clamp(ColorTheme, 0, 3));

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
        return new(value.Intensity, value.Sensitivity, value.Glow, start, end);
    }
}

internal sealed class AppSettingsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "settings.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

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
            value.SelectedWallpaperId ??= "built-in-ambient";
            value.Visualizers = (value.Visualizers ?? []).Where(item => item.Value is not null)
                .ToDictionary(item => item.Key, item => item.Value.Normalize());
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
