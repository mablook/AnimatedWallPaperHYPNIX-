using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class AppWindowFilterTests
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    [Fact]
    public void PlainAppWindowIsEligible()
        => Assert.True(AppWindowFilter.IsEligibleAppWindow(0));

    [Fact]
    public void NormalMaximizedBrowserIsEligible()
        => Assert.True(AppWindowFilter.IsEligibleAppWindow(WsExAppWindow));

    [Fact]
    public void ToolWindowIsNotEligible()
        => Assert.False(AppWindowFilter.IsEligibleAppWindow(WsExToolWindow));

    [Fact]
    public void ClickThroughLayeredOverlayIsNotEligible()
        => Assert.False(AppWindowFilter.IsEligibleAppWindow(WsExLayered | WsExTransparent));

    // The NVIDIA GeForce overlay ships two full-screen windows on the primary monitor; both are tool
    // windows and must be ignored so the wallpaper keeps animating with no real app in the foreground.
    [Fact]
    public void NvidiaOverlayTransparentToolWindowIsNotEligible()
        => Assert.False(AppWindowFilter.IsEligibleAppWindow(WsExLayered | WsExTransparent | WsExToolWindow));

    [Fact]
    public void NvidiaOverlayNoActivateToolWindowIsNotEligible()
        => Assert.False(AppWindowFilter.IsEligibleAppWindow(WsExLayered | WsExNoActivate | WsExToolWindow));

    // A layered window that is NOT click-through and NOT a tool window is still a real app (some apps use
    // per-pixel alpha) and should remain eligible.
    [Fact]
    public void OpaqueLayeredAppWindowIsEligible()
        => Assert.True(AppWindowFilter.IsEligibleAppWindow(WsExLayered));
}
