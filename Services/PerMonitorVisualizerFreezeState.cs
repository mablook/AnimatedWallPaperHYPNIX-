namespace AnimatedWallPaper.Services;

internal sealed class PerMonitorVisualizerFreezeState
{
    private readonly object _sync = new();
    private readonly Dictionary<int, Snapshot> _frozen = new();

    // Freezes each newly paused monitor at the current instant, keeps already-frozen monitors at their
    // ORIGINAL instant (so they never jump when another monitor is added), and releases monitors that
    // are no longer paused.
    public void Update(IReadOnlyCollection<int> pausedMonitors, double currentTimeSeconds, float[] currentBands)
    {
        lock (_sync)
        {
            foreach (var index in _frozen.Keys.Where(index => !pausedMonitors.Contains(index)).ToArray())
            {
                _frozen.Remove(index);
            }

            foreach (var index in pausedMonitors)
            {
                if (!_frozen.ContainsKey(index))
                {
                    _frozen[index] = new Snapshot(currentTimeSeconds, (float[])currentBands.Clone());
                }
            }
        }
    }

    public bool TryGetFrozenTime(int monitorIndex, out double timeSeconds)
    {
        lock (_sync)
        {
            if (_frozen.TryGetValue(monitorIndex, out var snapshot))
            {
                timeSeconds = snapshot.TimeSeconds;
                return true;
            }

            timeSeconds = 0;
            return false;
        }
    }

    public VisualizerFrameSample Resolve(
        int monitorIndex, double currentTimeSeconds, float[] currentBands)
    {
        lock (_sync)
        {
            return _frozen.TryGetValue(monitorIndex, out var snapshot)
                ? new VisualizerFrameSample(snapshot.TimeSeconds, (float[])snapshot.Bands.Clone(), true)
                : new VisualizerFrameSample(currentTimeSeconds, (float[])currentBands.Clone(), false);
        }
    }

    private readonly record struct Snapshot(double TimeSeconds, float[] Bands);
}

internal sealed record VisualizerFrameSample(double TimeSeconds, float[] Bands, bool IsFrozen);
