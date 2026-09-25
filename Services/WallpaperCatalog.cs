using System.IO;
using System.Text.Json;

namespace AnimatedWallPaper.Services;

internal sealed record WallpaperEntry(string Id, string Title, WallpaperKind Kind, string? PreviewPath,
    string? BackgroundPath = null, string? VideoPath = null, string? PackageDirectory = null,
    VisualizerPreferences? Defaults = null)
{
    public bool IsVisualizer => Kind is not (WallpaperKind.BuiltIn or WallpaperKind.ExampleVideo);
    // A solid-color/image background composites behind every visualizer that renders emission over
    // black (the shader effects, screen-blended) and the classic GDI visualizer. The two Effekseer
    // fire effects and video (which is itself the background) opt out.
    public bool SupportsBackground => Kind is WallpaperKind.LivingFire
        or WallpaperKind.NeonRibbons or WallpaperKind.LiquidOrbs or WallpaperKind.EventHorizon
        or WallpaperKind.FractalPyramid or WallpaperKind.Kaleidoscope or WallpaperKind.Lotus
        or WallpaperKind.SpectralBloom or WallpaperKind.AethelisVisualizer or WallpaperKind.VisualizerDemo;
    public bool SupportsSparks => Kind == WallpaperKind.LivingFire;
    // Size/position works on every visualizer whose renderer applies Scale/OffsetX/OffsetY.
    // The Effekseer effects (Fire Burst, Flamethrower Ring V2) author their motion in the
    // effect itself, so they intentionally opt out until the effect exposes a transform.
    public bool SupportsLayoutControls => Kind is WallpaperKind.NeonRibbons or WallpaperKind.LiquidOrbs
        or WallpaperKind.EventHorizon or WallpaperKind.FractalPyramid or WallpaperKind.Kaleidoscope or WallpaperKind.Lotus
        or WallpaperKind.LivingFire or WallpaperKind.SpectralBloom or WallpaperKind.AethelisVisualizer or WallpaperKind.VisualizerDemo;
    // Color palettes drive every visualizer except the two Effekseer fire effects, whose
    // colors live in the authored particle effect and are not recolored from the palette.
    public bool SupportsColorTheme => IsVisualizer
        && Kind is not (WallpaperKind.AethelisFlameBurst or WallpaperKind.FlamethrowerRingV2);
    // Glow drives the bloom/halo of every visualizer except the two Effekseer fire effects:
    // their background shader pass declares Glow but never reads it, and the native particle
    // update takes no glow parameter, so the control is hidden there to keep every shown
    // control effective.
    public bool SupportsGlow => IsVisualizer
        && Kind is not (WallpaperKind.AethelisFlameBurst or WallpaperKind.FlamethrowerRingV2);
    public string Description => Kind == WallpaperKind.ExampleVideo ? "Local video · muted · fit per display" :
        IsVisualizer ? "Audio reactive · system output" : "Native procedural wallpaper";
}

internal static class WallpaperCatalog
{
    // JsonSerializerOptions is thread-safe once configured; cache it instead of allocating per call.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<WallpaperEntry> LoadBuiltIns(string? root = null)
    {
        root ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers");
        var entries = new List<WallpaperEntry>();
        if (!Directory.Exists(root)) return entries;
        foreach (var path in Directory.EnumerateFiles(root, "wallpaper.json", SearchOption.AllDirectories).Order())
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var json = doc.RootElement;
                if (!json.TryGetProperty("kind", out var kindValue) ||
                    !Enum.TryParse<WallpaperKind>(kindValue.GetString(), out var kind) ||
                    !Enum.IsDefined(kind) || kind == WallpaperKind.VolumetricFire) continue;
                if (json.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean()) continue;
                var folder = Path.GetDirectoryName(path)!;
                entries.Add(new(json.GetProperty("id").GetString()!, json.GetProperty("title").GetString()!, kind,
                    Path.GetFullPath(Path.Combine(folder, json.GetProperty("preview").GetString()!)),
                    json.TryGetProperty("background", out var background) ? Path.Combine(folder, background.GetString()!) : null,
                    // Fire wallpapers default to the warm palette so palette tinting keeps the
                    // approved incandescent look; other themes recolor the flame from there.
                    Defaults: kind==WallpaperKind.LivingFire?new VisualizerPreferences(Glow:0,ColorTheme:1)
                        :kind==WallpaperKind.AethelisVisualizer?new VisualizerPreferences(ColorTheme:1):null));
            }
            catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidOperationException)
            { AppLog.WriteException($"Built-in manifest skipped: {path}", exception); }
        }
        return entries.OrderBy(entry => entry.Kind).ToArray();
    }

    public static WallpaperEntry FromPackage(WallpaperPackageRegistration registration)
    {
        if (registration.Status != WallpaperPackageStatus.Ready || registration.Manifest is not { } manifest)
            throw new InvalidDataException(registration.Error ?? "Package is not ready.");
        var presetPath = Path.Combine(registration.PackageDirectory, manifest.Entrypoint!);
        if (new FileInfo(presetPath).Length > 65536) throw new InvalidDataException("Preset JSON is too large.");
        var preferences = JsonSerializer.Deserialize<VisualizerPreferences>(File.ReadAllText(presetPath),
            JsonOptions)?.Normalize() ?? new();
        return new("package:" + manifest.Id, manifest.Title!, WallpaperKind.VisualizerDemo,
            Path.Combine(registration.PackageDirectory, manifest.Preview!),
            manifest.Background is null ? null : Path.Combine(registration.PackageDirectory, manifest.Background),
            PackageDirectory: registration.PackageDirectory, Defaults: preferences);
    }

    public static WallpaperRequest Request(WallpaperEntry entry, AppSettings settings)
    {
        // Revalidate at use time: a watcher snapshot may precede a file replacement.
        if (entry.PackageDirectory is not null)
            entry = FromPackage(WallpaperPackageValidator.Validate(entry.PackageDirectory));
        var preferences = settings.Visualizers.GetValueOrDefault(entry.Id) ?? entry.Defaults ?? new();
        return new(entry.Id, entry.Kind, settings.FramesPerSecond, entry.VideoPath,
            settings.MediaToolsDirectory, entry.BackgroundPath, preferences.ToSettings());
    }
}
