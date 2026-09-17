using System.Drawing;

namespace AnimatedWallPaper.Services;

internal sealed record VisualizerSettings(
    float Intensity,
    float Sensitivity,
    float Glow,
    Color StartColor,
    Color EndColor,
    float Scale = 1f,
    float OffsetX = 0f,
    float OffsetY = 0f, VisualizerBackground? Background = null, bool Sparks = true)
{
    public static VisualizerSettings Default { get; } = new VisualizerPreferences().ToSettings();
}
