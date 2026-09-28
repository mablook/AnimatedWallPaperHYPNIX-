using System.Numerics;

namespace AnimatedWallPaper.Services;

// Owned by the logical preview, never by GPU resources. Publishing an immutable
// trajectory lets all four volume passes evaluate past/future ticks consistently.
internal sealed class OceanCloudMotion(float initialWind = 9)
{
    public OceanCloudTrajectory Trajectory { get; private set; } = new(initialWind);
    private float _requested = OceanCloudTrajectory.CleanWind(initialWind);

    public void SetWind(double weatherTime, float wind)
    {
        wind = OceanCloudTrajectory.CleanWind(wind);
        if (wind == _requested) return;
        _requested = wind;
        // Both cache intervals divide one second. Start after the currently
        // displayed endpoints, so replacing a predicted future cannot jump them.
        // This <=1 s response delay is independent of render quality/frame rate.
        var start = Math.Floor(OceanCloudTrajectory.CleanTime(weatherTime)) + 1;
        Trajectory = Trajectory.ChangeAt(start, wind);
    }
}

internal sealed record OceanCloudTrajectory
{
    private sealed record Segment(double Start, double Distance, double From, double To);
    private readonly Segment[] _segments;
    public float InitialWind { get; }
    public const double RampSeconds = 3;
    public OceanCloudTrajectory(float wind) { InitialWind = CleanWind(wind); _segments = []; }
    private OceanCloudTrajectory(float wind, Segment[] segments) { InitialWind = wind; _segments = segments; }
    internal static float CleanWind(float wind) => float.IsFinite(wind) ? Math.Clamp(wind, 0, 30) : 9;
    internal static double CleanTime(double time) => double.IsFinite(time) ? Math.Max(0, time) : 0;

    public (double Distance, double Speed) Evaluate(double time)
    {
        time = CleanTime(time);
        var lo = 0; var hi = _segments.Length - 1;
        while (lo <= hi) { var m = (lo + hi) / 2; if (_segments[m].Start <= time) lo = m + 1; else hi = m - 1; }
        if (hi < 0) return (InitialWind * time, InitialWind);
        var s = _segments[hi]; var dt = time - s.Start;
        var ramp = Math.Min(dt, RampSeconds); var acceleration = (s.To - s.From) / RampSeconds;
        return (s.Distance + s.From * ramp + .5 * acceleration * ramp * ramp + s.To * Math.Max(0, dt - RampSeconds),
            s.From + acceleration * ramp);
    }

    internal OceanCloudTrajectory ChangeAt(double start, float target)
    {
        // Coalesce edits to a pending event; retain every past segment for seek.
        var previous = _segments.Where(s => s.Start < start).ToArray();
        var history = new OceanCloudTrajectory(InitialWind, previous);
        var atStart = history.Evaluate(start);
        return new(InitialWind, [.. previous, new(start, atStart.Distance, atStart.Speed, CleanWind(target))]);
    }

    public (Vector4 Travel, Vector4 Evolution, Vector4 Shear) Constants(double time, OceanCloudType type)
    {
        time = CleanTime(time);
        var distance = Evaluate(time).Distance * .001; // km, including the full wind history
        var profile = Profile(type);
        // Reduce each independent periodic coordinate in double, not float time.
        var x = distance * (3 / Math.Sqrt(10)); var z = distance / Math.Sqrt(10);
        var travel = new Vector4((float)(x % 1600), (float)(z % 1600), (float)((distance / profile.RenewalKm) % 1), 0);
        var rate = type switch { OceanCloudType.Cumulus => 1d, OceanCloudType.Stratus => .30, _ => .55 };
        // Slow bounded motion of the existing warp/erosion fields in material
        // coordinates. Independent periods prevent a short, synchronized loop.
        double Phase(double period) => 2 * Math.PI * ((time * rate % period) / period);
        var evolution = new Vector4((float)(1.8 * Math.Sin(Phase(601))),
            (float)(.65 * Math.Sin(Phase(887))), (float)(1.3 * Math.Sin(Phase(997))), profile.RenewalKm);
        return (travel, evolution, new(profile.ShearX, profile.ShearZ, 0, 0));
    }

    internal static (float RenewalKm, float ShearX, float ShearZ) Profile(OceanCloudType type)
    {
        var (length, along, across) = type switch
        {
            OceanCloudType.Cumulus => (3f, .20f, .085f),
            OceanCloudType.Stratus => (9f, .10f, .025f),
            _ => (6f, .15f, .050f)
        };
        const float x = .948683298f, z = .316227766f;
        return (length, x * along - z * across, z * along + x * across);
    }
}
