namespace AnimatedWallPaper.Services;

// Decides whether a top-level window represents an application the user is actually using (and that could
// hide the wallpaper). Auxiliary overlays cover a monitor visually but are NOT apps, so they must never
// pause the wallpaper. The classic offenders are tool windows and click-through layered overlays such as
// the NVIDIA GeForce overlay (class "CEF-OSC-WIDGET", WS_EX_TOOLWINDOW + WS_EX_LAYERED), which otherwise
// makes the primary monitor look permanently "covered" even with no app in the foreground.
internal static class AppWindowFilter
{
    private const int WsExTransparent = 0x00000020; // click-through
    private const int WsExToolWindow = 0x00000080;  // auxiliary window, not shown in taskbar/alt-tab
    private const int WsExLayered = 0x00080000;

    public static bool IsEligibleAppWindow(int extendedStyle)
    {
        // Tool windows are palettes/overlays, never the app the user is working in.
        if ((extendedStyle & WsExToolWindow) != 0)
        {
            return false;
        }

        // Click-through layered overlays draw over a monitor but pass input to whatever is underneath.
        if ((extendedStyle & WsExLayered) != 0 && (extendedStyle & WsExTransparent) != 0)
        {
            return false;
        }

        return true;
    }
}
