namespace AnimatedWallPaper.Services;

internal enum OceanMoment { Dawn, Day, Sunset, Moon }

// Product preferences are independent of audio-reactive settings and GPU resources.
internal sealed record OceanPreferences(
    int Version = 1, OceanMoment? Moment = OceanMoment.Dawn,
    DateTimeOffset? SkyUtc = null, bool DayCycle = true, int DayMinutes = 60,
    float Agitation = .65f, float WaveSpeed = 1, float Coverage = .25f,
    OceanCloudType Clouds = OceanCloudType.Stratocumulus, float Wind = 9,
    OceanFog Fog = OceanFog.Maritime, bool Horizon = true,
    OceanQuality Quality = OceanQuality.Balanced, bool Bloom = true, bool HorizonMagnification = true)
{
    public static DateTimeOffset MomentUtc(OceanMoment moment) => new(2026, 9, 27,
        moment switch { OceanMoment.Day => 12, OceanMoment.Sunset => 18, OceanMoment.Moon => 19, _ => 6 },
        moment switch { OceanMoment.Day => 30, OceanMoment.Sunset => 5, OceanMoment.Moon => 8, _ => 59 }, 0, TimeSpan.Zero);

    public OceanPreferences Normalize() => Version != 1 ? new OceanPreferences().Normalize() : this with
    {
        Moment = Moment is { } moment && Enum.IsDefined(moment) ? moment : null,
        SkyUtc = SkyUtc is { Year: >= 1900 and <= 2100 } utc ? utc.ToUniversalTime() : MomentUtc(Moment ?? OceanMoment.Dawn),
        DayMinutes = DayMinutes is 10 or 20 or 60 or 120 ? DayMinutes : 60,
        Agitation = float.IsFinite(Agitation) ? Math.Clamp(Agitation, 0, 1) : .65f,
        WaveSpeed = float.IsFinite(WaveSpeed) ? Math.Clamp(WaveSpeed, .25f, 1.5f) : 1,
        Coverage = float.IsFinite(Coverage) ? Math.Clamp(Coverage, 0, 1) : .25f,
        Clouds = Enum.IsDefined(Clouds) ? Clouds : OceanCloudType.Stratocumulus,
        Wind = float.IsFinite(Wind) ? Math.Clamp(Wind, 0, 30) : 9,
        Fog = Enum.IsDefined(Fog) ? Fog : OceanFog.Maritime,
        Quality = Enum.IsDefined(Quality) ? Quality : OceanQuality.Balanced
    };

    public OceanSettings ToRenderSettings()
    {
        var p = Normalize();
        return new(Agitation:p.Agitation, Speed:p.WaveSpeed, Quality:p.Quality, Horizon:p.Horizon,
            Bloom:p.Bloom, HorizonMagnification:p.HorizonMagnification,
            Celestial:OceanCelestialSettings.Default with { EpochUtc=p.SkyUtc!.Value, TimeScale=1440d/p.DayMinutes },
            Weather:new(p.Clouds,p.Fog,p.Coverage,p.Wind));
    }
}
