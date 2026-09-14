namespace AnimatedWallPaper.Services;

// Pure geometry that classifies how a window sits on a monitor, so the per-monitor pause rule can be unit
// tested without Win32. A window "covers" an area when its rectangle spans that area within a small
// tolerance (borderless/fullscreen windows usually match the monitor exactly; maximized windows fill the
// work area and often bleed a few pixels past it).
internal static class MonitorCoverage
{
    public enum Coverage
    {
        None,
        Maximized,
        Fullscreen
    }

    public readonly record struct Rectangle(int Left, int Top, int Right, int Bottom);

    public static Coverage Classify(Rectangle window, Rectangle monitor, Rectangle work, int tolerance = 2)
    {
        if (window.Right <= window.Left || window.Bottom <= window.Top)
        {
            return Coverage.None;
        }

        if (Covers(window, monitor, tolerance))
        {
            return Coverage.Fullscreen;
        }

        return Covers(window, work, tolerance) ? Coverage.Maximized : Coverage.None;
    }

    private static bool Covers(Rectangle window, Rectangle area, int tolerance) =>
        window.Left <= area.Left + tolerance &&
        window.Top <= area.Top + tolerance &&
        window.Right >= area.Right - tolerance &&
        window.Bottom >= area.Bottom - tolerance;
}
