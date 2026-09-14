using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class MonitorCoverageTests
{
    // A 1920x1080 monitor with a 40px taskbar at the bottom (work area 1920x1040).
    private static readonly MonitorCoverage.Rectangle Monitor = new(0, 0, 1920, 1080);
    private static readonly MonitorCoverage.Rectangle Work = new(0, 0, 1920, 1040);

    [Fact]
    public void FullscreenWindowCoveringTheWholeMonitorIsFullscreen()
    {
        var window = new MonitorCoverage.Rectangle(0, 0, 1920, 1080);
        Assert.Equal(MonitorCoverage.Coverage.Fullscreen, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void BorderlessWindowSpillingPastTheMonitorIsFullscreen()
    {
        var window = new MonitorCoverage.Rectangle(-1, -1, 1921, 1081);
        Assert.Equal(MonitorCoverage.Coverage.Fullscreen, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void MaximizedWindowCoveringOnlyTheWorkAreaIsMaximized()
    {
        // Maximized windows fill the work area (above the taskbar) and bleed a few pixels past its edges.
        var window = new MonitorCoverage.Rectangle(-8, -8, 1928, 1048);
        Assert.Equal(MonitorCoverage.Coverage.Maximized, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void SmallFloatingWindowCoversNothing()
    {
        var window = new MonitorCoverage.Rectangle(100, 100, 500, 400);
        Assert.Equal(MonitorCoverage.Coverage.None, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void TallButNarrowWindowCoversNothing()
    {
        var window = new MonitorCoverage.Rectangle(0, 0, 900, 1080);
        Assert.Equal(MonitorCoverage.Coverage.None, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void ZeroSizedWindowCoversNothing()
    {
        var window = new MonitorCoverage.Rectangle(10, 10, 10, 10);
        Assert.Equal(MonitorCoverage.Coverage.None, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void CoverageWithinToleranceStillCountsAsFullscreen()
    {
        var window = new MonitorCoverage.Rectangle(2, 2, 1918, 1078);
        Assert.Equal(MonitorCoverage.Coverage.Fullscreen, MonitorCoverage.Classify(window, Monitor, Work));
    }

    [Fact]
    public void ClassificationWorksOnASecondMonitorWithOffsetOrigin()
    {
        var monitor = new MonitorCoverage.Rectangle(1920, 0, 3840, 1080);
        var work = new MonitorCoverage.Rectangle(1920, 0, 3840, 1040);

        var fullscreen = new MonitorCoverage.Rectangle(1920, 0, 3840, 1080);
        var maximized = new MonitorCoverage.Rectangle(1912, -8, 3848, 1048);
        var floating = new MonitorCoverage.Rectangle(2000, 100, 2400, 500);

        Assert.Equal(MonitorCoverage.Coverage.Fullscreen, MonitorCoverage.Classify(fullscreen, monitor, work));
        Assert.Equal(MonitorCoverage.Coverage.Maximized, MonitorCoverage.Classify(maximized, monitor, work));
        Assert.Equal(MonitorCoverage.Coverage.None, MonitorCoverage.Classify(floating, monitor, work));
    }
}
