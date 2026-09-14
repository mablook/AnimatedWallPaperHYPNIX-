using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class PlaybackPolicyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void BatteryOverridesPerDisplayPause(int mode)
    {
        var result = PlaybackPolicy.Evaluate(mode, true, true, true, true, true, 1);
        Assert.True(result.PauseAll);
        Assert.Null(result.PausedMonitor);
        Assert.Equal("battery power", result.Reason);
    }

    [Fact]
    public void BatteryDoesNotPauseWhenOptionDisabled()
    {
        var result = PlaybackPolicy.Evaluate(0, false, false, false, true, true, 1);
        Assert.False(result.PauseAll);
        Assert.Null(result.PausedMonitor);
    }

    [Fact]
    public void ForegroundPauseMovesBetweenDisplays()
    {
        Assert.Equal(0, PlaybackPolicy.Evaluate(2, false, true, false, false, true, 0).PausedMonitor);
        Assert.Equal(1, PlaybackPolicy.Evaluate(2, false, true, false, false, true, 1).PausedMonitor);
        Assert.Null(PlaybackPolicy.Evaluate(2, false, false, false, false, true, null).PausedMonitor);
    }

    [Fact]
    public void LockedSessionPausesEvenWithAppPolicyDisabled()
        => Assert.True(PlaybackPolicy.Evaluate(0, false, false, false, false, true, 1, sessionLocked: true).PauseAll);

    [Fact]
    public void MissingForegroundDisplayFallsBackToGlobalPause()
        => Assert.True(PlaybackPolicy.Evaluate(2, false, true, false, false, true, null).PauseAll);
}
