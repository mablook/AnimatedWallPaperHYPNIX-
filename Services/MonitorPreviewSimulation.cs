namespace AnimatedWallPaper.Services;

internal sealed record SimulatedPreviewDisplay(string Id, string Label, double Aspect, string? WallpaperId);

// Preview-only state: these IDs never represent Windows displays or persisted assignments.
internal sealed class MonitorPreviewSimulation
{
    internal static readonly double[] SupportedAspects = [16d / 9, 21d / 9, 32d / 9, 9d / 16];
    private static readonly string[] PreferredWallpaperIds =
        ["living-fire", "kaleidoscope", "spectral-bloom", "neon-ribbons"];
    private readonly string?[] _wallpaperIds = new string?[4];
    private readonly double[] _aspects = [16d / 9, 16d / 9, 9d / 16, 21d / 9];

    public IReadOnlyList<SimulatedPreviewDisplay> Displays { get; private set; } =
        Array.Empty<SimulatedPreviewDisplay>();

    public void SetCount(int count, IReadOnlyList<WallpaperEntry> catalog)
    {
        if (count is not (3 or 4))
            throw new ArgumentOutOfRangeException(nameof(count), count, "Simulate three or four displays.");
        ArgumentNullException.ThrowIfNull(catalog);

        // Keep the fourth choice while showing three displays, but discard selections whose
        // catalog entries have disappeared. Preserve intentional duplicate choices as well.
        var availableIds = catalog.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < _wallpaperIds.Length; index++)
            if (_wallpaperIds[index] is { } selected && !availableIds.Contains(selected))
                _wallpaperIds[index] = null;

        var usedIds = _wallpaperIds.OfType<string>().ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < _wallpaperIds.Length; index++)
        {
            if (_wallpaperIds[index] is not null) continue;
            var preferred = PreferredWallpaperIds[index];
            var selected = availableIds.Contains(preferred) && !usedIds.Contains(preferred)
                ? preferred
                : PreferredWallpaperIds.FirstOrDefault(id => availableIds.Contains(id) && !usedIds.Contains(id))
                    ?? catalog.FirstOrDefault(entry => !usedIds.Contains(entry.Id))?.Id
                    ?? (catalog.Count > 0 ? catalog[0].Id : null);
            _wallpaperIds[index] = selected;
            if (selected is not null) usedIds.Add(selected);
        }

        RefreshDisplays(count);
    }

    public bool SelectWallpaper(string displayId, string wallpaperId, IReadOnlyList<WallpaperEntry> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var index = -1;
        for (var candidate = 0; candidate < Displays.Count; candidate++)
            if (string.Equals(Displays[candidate].Id, displayId, StringComparison.Ordinal))
            {
                index = candidate;
                break;
            }

        if (index < 0 || string.IsNullOrWhiteSpace(wallpaperId)
            || !catalog.Any(entry => string.Equals(entry.Id, wallpaperId, StringComparison.Ordinal)))
            return false;

        _wallpaperIds[index] = wallpaperId;
        RefreshDisplays(Displays.Count);
        return true;
    }

    public bool SelectAspect(string displayId, double aspect)
    {
        var index = Array.FindIndex(Displays.ToArray(), display => display.Id == displayId);
        if (index < 0 || !SupportedAspects.Contains(aspect)) return false;
        _aspects[index] = aspect;
        RefreshDisplays(Displays.Count);
        return true;
    }

    private void RefreshDisplays(int count)
    {
        var displays = new SimulatedPreviewDisplay[count];
        for (var index = 0; index < count; index++)
            displays[index] = new($"preview-sim-{index + 1}", $"Display {index + 1}",
                _aspects[index], _wallpaperIds[index]);
        Displays = Array.AsReadOnly(displays);
    }
}
