using System.Drawing;

namespace AnimatedWallPaper.Services;

internal sealed record VisualizerSettings(
    float Intensity,
    float Sensitivity,
    float Glow,
    Color StartColor,
    Color EndColor)
{
    public static VisualizerSettings Default { get; } = new(
        1f, 1f, 0.55f,
        Color.FromArgb(88, 205, 255),
        Color.FromArgb(104, 80, 255));
}
