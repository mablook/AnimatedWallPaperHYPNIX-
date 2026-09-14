using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class PerMonitorVisualizerFreezeStateTests
{
    [Fact]
    public void PausedMonitorKeepsBothTimeAndFftSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        var bandsAtPause = new[] { 0.1f, 0.4f, 0.8f };

        state.Update(new[] { 1 }, 12.5, bandsAtPause);
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
        state.Update(new[] { 1 }, 12.5, new[] { 0.2f, 0.3f });

        var sample = state.Resolve(0, 21.25, new[] { 0.7f, 0.9f });

        Assert.False(sample.IsFrozen);
        Assert.Equal(21.25, sample.TimeSeconds);
        Assert.Equal(new[] { 0.7f, 0.9f }, sample.Bands);
    }

    [Fact]
    public void MultipleMonitorsFreezeIndependentlyAndKeepTheirOwnInstant()
    {
        var state = new PerMonitorVisualizerFreezeState();
        // Monitor 0 becomes occupied first at t=5.
        state.Update(new[] { 0 }, 5, new[] { 0.1f });
        // Monitor 1 becomes occupied later at t=9; monitor 0 must keep its ORIGINAL t=5 snapshot.
        state.Update(new[] { 0, 1 }, 9, new[] { 0.6f });

        var first = state.Resolve(0, 30, new[] { 1f });
        var second = state.Resolve(1, 30, new[] { 1f });

        Assert.True(first.IsFrozen);
        Assert.Equal(5, first.TimeSeconds);
        Assert.True(second.IsFrozen);
        Assert.Equal(9, second.TimeSeconds);
    }

    [Fact]
    public void ClearingOneMonitorLeavesTheOtherFrozen()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(new[] { 0, 1 }, 4, new[] { 0.2f });
        // Monitor 0 clears (app minimized); monitor 1 stays occupied and keeps its original instant.
        state.Update(new[] { 1 }, 8, new[] { 0.6f });

        var cleared = state.Resolve(0, 9, new[] { 0.85f });
        var stillFrozen = state.Resolve(1, 9, new[] { 0.85f });

        Assert.False(cleared.IsFrozen);
        Assert.Equal(9, cleared.TimeSeconds);
        Assert.True(stillFrozen.IsFrozen);
        Assert.Equal(4, stillFrozen.TimeSeconds);
    }

    [Fact]
    public void ClearingPauseReleasesFrozenSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(new[] { 0 }, 4, new[] { 0.2f });
        state.Update(Array.Empty<int>(), 8, new[] { 0.6f });

        var sample = state.Resolve(0, 9, new[] { 0.85f });

        Assert.False(sample.IsFrozen);
        Assert.Equal(9, sample.TimeSeconds);
        Assert.Equal(new[] { 0.85f }, sample.Bands);
    }

    [Fact]
    public void TryGetFrozenTimeReflectsPauseState()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(new[] { 2 }, 7.5, new[] { 0.3f });

        Assert.True(state.TryGetFrozenTime(2, out var frozen));
        Assert.Equal(7.5, frozen);
        Assert.False(state.TryGetFrozenTime(0, out _));
    }

    [Fact]
    public void ReturnedBandsCannotMutateStoredSnapshot()
    {
        var state = new PerMonitorVisualizerFreezeState();
        state.Update(new[] { 0 }, 2, new[] { 0.25f });

        var first = state.Resolve(0, 3, new[] { 1f });
        first.Bands[0] = 0.99f;
        var second = state.Resolve(0, 4, new[] { 1f });

        Assert.Equal(0.25f, second.Bands[0]);
    }
}
