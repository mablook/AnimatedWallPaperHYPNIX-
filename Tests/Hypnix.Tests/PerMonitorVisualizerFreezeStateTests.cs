using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class PerMonitorVisualizerFreezeStateTests
{
    [Fact]
    public void PausedMonitorKeepsBothTimeAndFftSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        var bandsAtPause = new[] { 0.1f, 0.4f, 0.8f };

        state.Update(1, 12.5, bandsAtPause);
        bandsAtPause[0] = 0.9f;
        var sample = state.Resolve(1, 20, new[] { 1f, 1f, 1f });

        Assert.True(sample.IsFrozen);
        Assert.Equal(12.5, sample.TimeSeconds);
        Assert.Equal(new[] { 0.1f, 0.4f, 0.8f }, sample.Bands);
    }

    [Fact]
    public void OtherMonitorContinuesWithCurrentTimeAndAudio()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(1, 12.5, new[] { 0.2f, 0.3f });

        var sample = state.Resolve(0, 21.25, new[] { 0.7f, 0.9f });

        Assert.False(sample.IsFrozen);
        Assert.Equal(21.25, sample.TimeSeconds);
        Assert.Equal(new[] { 0.7f, 0.9f }, sample.Bands);
    }

    [Fact]
    public void ClearingPauseReleasesFrozenSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(0, 4, new[] { 0.2f });
        state.Update(null, 8, new[] { 0.6f });

        var sample = state.Resolve(0, 9, new[] { 0.85f });

        Assert.False(sample.IsFrozen);
        Assert.Equal(9, sample.TimeSeconds);
        Assert.Equal(new[] { 0.85f }, sample.Bands);
    }

    [Fact]
    public void ReturnedBandsCannotMutateStoredSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(0, 2, new[] { 0.25f });

        var first = state.Resolve(0, 3, new[] { 1f });
        first.Bands[0] = 0.99f;
        var second = state.Resolve(0, 4, new[] { 1f });

        Assert.Equal(0.25f, second.Bands[0]);
    }
}
