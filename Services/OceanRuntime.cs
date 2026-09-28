using System.Diagnostics;

namespace AnimatedWallPaper.Services;

internal sealed record OceanSnapshot(OceanPreferences Preferences, double WaterTime, double WeatherTime,
    DateTimeOffset SkyUtc, OceanCloudTrajectory Clouds);

// One logical output owns this state. A new GPU host takes the lease only at reveal;
// disposal of an old host cannot pause its replacement. All published frames are immutable.
internal sealed class OceanRuntime
{
    private readonly object _gate = new();
    private readonly Func<double> _now;
    private OceanPreferences _preferences;
    private OceanSettings _renderSettings;
    private double _water, _weather, _last;
    private DateTimeOffset _sky;
    private OceanCloudTrajectory _clouds;
    private object? _owner;
    private bool _paused;

    public OceanRuntime(OceanPreferences? preferences = null, Func<double>? now = null)
    {
        _now = now ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _preferences = (preferences ?? new()).Normalize();
        _renderSettings = _preferences.ToRenderSettings();
        _sky = _preferences.SkyUtc!.Value;
        _clouds = new(_preferences.Wind);
        _last = _now();
    }

    private void Advance()
    {
        var now = _now();
        var elapsed = Math.Max(0, now - _last); _last = now;
        if (_owner is null || _paused) return;
        _water += elapsed * _preferences.WaveSpeed;
        _weather += elapsed;
        if (_preferences.DayCycle)
        {
            // Bound the supported ephemeris range even after a long unattended run.
            var seconds = Math.Min(elapsed * 1440 / _preferences.DayMinutes,
                (new DateTimeOffset(2100,12,31,23,59,59,TimeSpan.Zero)-_sky).TotalSeconds);
            _sky = _sky.AddSeconds(Math.Max(0, seconds));
        }
    }

    public void Activate(object owner, bool paused)
    { lock (_gate) { Advance(); _owner=owner; _paused=paused; } }
    public void SetPaused(object owner, bool paused)
    { lock (_gate) { if (!ReferenceEquals(_owner,owner)) return; Advance(); _paused=paused; } }
    public void Release(object owner)
    { lock (_gate) { if (!ReferenceEquals(_owner,owner)) return; Advance(); _owner=null; } }

    public void Update(OceanPreferences preferences)
    {
        lock (_gate)
        {
            Advance(); var p=preferences.Normalize();
            if (p.SkyUtc != _preferences.SkyUtc) _sky=p.SkyUtc!.Value;
            if (p.Wind != _preferences.Wind) _clouds=_clouds.ChangeAt(Math.Floor(_weather)+1,p.Wind);
            _preferences=p; _renderSettings=p.ToRenderSettings();
        }
    }

    public void SeekSky(DateTimeOffset utc)
    {
        lock (_gate) { Advance(); _sky = (new OceanPreferences(SkyUtc:utc).Normalize()).SkyUtc!.Value; }
    }

    public (OceanSnapshot Snapshot, OceanSettings Settings) Frame()
    {
        lock (_gate)
        {
            Advance();
            return (new(_preferences,_water,_weather,_sky,_clouds),_renderSettings);
        }
    }

    public OceanRuntime Fork()
    {
        var s=Frame().Snapshot;
        return new(s.Preferences,_now) { _water=s.WaterTime, _weather=s.WeatherTime, _sky=s.SkyUtc, _clouds=s.Clouds };
    }

    public OceanPreferences Checkpoint()
    {
        var s=Frame().Snapshot;
        return s.Preferences with { SkyUtc=s.SkyUtc, Moment=s.Preferences.DayCycle ? null : s.Preferences.Moment };
    }
}
