using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class PlaybackPolicyTests
{
    private static readonly int[] None = Array.Empty<int>();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BatteryOverridesPerDisplayPause(int mode)
    {
        var result = PlaybackPolicy.Evaluate(mode, new[] { 0 }, new[] { 0, 1 }, true, true, perMonitor: true);
        Assert.True(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
        Assert.Equal("battery power", result.Reason);
    }

    [Fact]
    public void BatteryDoesNotPauseWhenOptionDisabled()
    {
        var result = PlaybackPolicy.Evaluate(0, None, new[] { 0, 1 }, false, true, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
    }

    [Fact]
    public void LockedSessionPausesEverythingEvenWithAppPolicyDisabled()
    {
        var result = PlaybackPolicy.Evaluate(0, new[] { 0 }, new[] { 0, 1 }, false, false, perMonitor: true, sessionLocked: true);
        Assert.True(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
        Assert.Equal("session locked", result.Reason);
    }

    [Fact]
    public void NeverModeKeepsEveryMonitorRunning()
    {
        var result = PlaybackPolicy.Evaluate(0, new[] { 0 }, new[] { 0, 1 }, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
        Assert.Equal("ready", result.Reason);
    }

    // Rule 1: "Active display only" + a covering app on one monitor freezes ONLY that monitor;
    // the clean monitor keeps animating.
    [Fact]
    public void ActiveDisplayOnlyFreezesOnlyTheOccupiedMonitor()
    {
        var result = PlaybackPolicy.Evaluate(2, None, new[] { 0 }, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Equal(new[] { 0 }, result.PausedMonitors);
    }

    // Rule 2: when every monitor is covered by an app, every monitor freezes at the same time.
    [Fact]
    public void ActiveDisplayOnlyFreezesEveryMonitorWhenAllAreCovered()
    {
        var result = PlaybackPolicy.Evaluate(2, None, new[] { 0, 1 }, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Equal(new[] { 0, 1 }, result.PausedMonitors);
    }

    // Rule 3: nothing covering any monitor (e.g. everything minimized) -> every monitor animates.
    [Fact]
    public void ActiveDisplayOnlyRunsEveryMonitorWhenNothingIsCovered()
    {
        var result = PlaybackPolicy.Evaluate(2, None, None, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
        Assert.Equal("ready", result.Reason);
    }

    [Fact]
    public void FullscreenOnlyModeIgnoresMerelyMaximizedMonitors()
    {
        // Monitor 0 is maximized (covered) but not fullscreen; monitor 1 is fullscreen.
        var result = PlaybackPolicy.Evaluate(1, new[] { 1 }, new[] { 0, 1 }, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Equal(new[] { 1 }, result.PausedMonitors);
    }

    [Fact]
    public void MaximizedOrFullscreenModeIncludesMaximizedMonitors()
    {
        var result = PlaybackPolicy.Evaluate(2, new[] { 1 }, new[] { 0, 1 }, false, false, perMonitor: true);
        Assert.False(result.PauseAll);
        Assert.Equal(new[] { 0, 1 }, result.PausedMonitors);
    }

    // "All displays" scope: a single covered monitor freezes the whole desktop.
    [Fact]
    public void AllDisplaysScopeFreezesEveryMonitorWhenAnyIsCovered()
    {
        var result = PlaybackPolicy.Evaluate(2, None, new[] { 1 }, false, false, perMonitor: false);
        Assert.True(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
        Assert.Equal("another app is active", result.Reason);
    }

    [Fact]
    public void AllDisplaysScopeStaysRunningWhenNothingIsCovered()
    {
        var result = PlaybackPolicy.Evaluate(2, None, None, false, false, perMonitor: false);
        Assert.False(result.PauseAll);
        Assert.Empty(result.PausedMonitors);
    }

    [Fact]
    public void ReasonNamesTheSinglePausedDisplay()
        => Assert.Equal("display 2 paused",
            PlaybackPolicy.Evaluate(2, None, new[] { 1 }, false, false, perMonitor: true).Reason);

    [Fact]
    public void ReasonCountsMultiplePausedDisplays()
        => Assert.Equal("2 displays paused",
            PlaybackPolicy.Evaluate(2, None, new[] { 0, 1 }, false, false, perMonitor: true).Reason);

    [Fact]
    public void DuplicateAndUnsortedMonitorIndicesAreNormalized()
    {
        var result = PlaybackPolicy.Evaluate(2, None, new[] { 1, 0, 1 }, false, false, perMonitor: true);
        Assert.Equal(new[] { 0, 1 }, result.PausedMonitors);
    }
}
