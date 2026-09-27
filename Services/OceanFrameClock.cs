namespace AnimatedWallPaper.Services;

// Own this per logical preview/output, outside the renderer. Recreating GPU resources
// never resets the ocean. Explicit transitions integrate time before changing speed/state.
internal sealed class OceanFrameClock
{
    private double _last;
    private bool _initialized;
    private bool _paused;
    private double _speed = 1;
    public double Time { get; private set; }
    public bool IsPaused => _paused;

    public double Advance(double timestamp)
    {
        if (!double.IsFinite(timestamp) || timestamp < 0) return Time;
        if (!_initialized) { _last = timestamp; _initialized = true; return Time; }
        if (timestamp < _last) return Time;
        if (!_paused) Time += (timestamp - _last) * _speed;
        _last = timestamp;
        return Time;
    }

    public void SetPaused(bool paused, double timestamp)
    {
        Advance(timestamp);
        _paused = paused;
    }

    public void SetSpeed(double speed, double timestamp)
    {
        Advance(timestamp);
        _speed = double.IsFinite(speed) ? Math.Clamp(speed, .25, 1.5) : 1;
    }
}
