namespace AnimatedWallPaper.Services;

internal sealed class PerMonitorVisualizerFreezeState
{
    private readonly object _sync = new();
    private int? _pausedMonitorIndex;
    private double _pausedAtSeconds;
    private float[]? _pausedBands;

    public void Update(int? monitorIndex, double currentTimeSeconds, float[] currentBands)
    {
        lock (_sync)
        {
            _pausedMonitorIndex = monitorIndex;
            _pausedAtSeconds = currentTimeSeconds;
            _pausedBands = monitorIndex is null ? null : (float[])currentBands.Clone();
        }
    }

    public VisualizerFrameSample Resolve(
        int monitorIndex, double currentTimeSeconds, float[] currentBands)
    {
        lock (_sync)
        {
            return _pausedMonitorIndex == monitorIndex && _pausedBands is not null
                ? new VisualizerFrameSample(_pausedAtSeconds, (float[])_pausedBands.Clone(), true)
                : new VisualizerFrameSample(currentTimeSeconds, (float[])currentBands.Clone(), false);
        }
    }
}

internal sealed record VisualizerFrameSample(double TimeSeconds, float[] Bands, bool IsFrozen);
