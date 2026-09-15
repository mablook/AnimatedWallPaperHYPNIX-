using System.Drawing;

namespace AnimatedWallPaper.Services;

internal sealed record VisualizerSettings(
    float Intensity,
    float Sensitivity,
    float Glow,
    Color StartColor,
    Color EndColor)
{
    public static VisualizerSettings Default { get; } = new VisualizerPreferences().ToSettings();
}
