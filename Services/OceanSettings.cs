namespace AnimatedWallPaper.Services;

internal enum OceanLighting { Sunset, Day, Moon, Overcast }
internal enum OceanQuality { Economy, Balanced, High }
internal enum OceanSurface { Spectral, Analytic }

// P1 settings are deliberately independent of audio-reactive VisualizerSettings.
internal sealed record OceanSettings(
    OceanLighting Lighting = OceanLighting.Sunset,
    float Agitation = 0.65f,
    float Speed = 1,
    OceanQuality Quality = OceanQuality.Balanced,
    bool Horizon = true,
    OceanSurface Surface = OceanSurface.Spectral,
    bool Atmosphere = true,
    bool RefinedSky = true,
    bool GibbousMoon = false,
    bool Bloom = true,
    bool HorizonMagnification = true,
    OceanCelestialSettings? Celestial = null,
    OceanWeatherSettings? Weather = null)
{
    public OceanSettings Normalize() => this with
    {
        Lighting = Enum.IsDefined(Lighting) ? Lighting : OceanLighting.Sunset,
        Quality = Enum.IsDefined(Quality) ? Quality : OceanQuality.Balanced,
        Surface = Enum.IsDefined(Surface) ? Surface : OceanSurface.Spectral,
        Agitation = float.IsFinite(Agitation) ? Math.Clamp(Agitation, 0, 1) : 0.65f,
        Speed = float.IsFinite(Speed) ? Math.Clamp(Speed, 0.25f, 1.5f) : 1,
        Celestial = Celestial?.Normalize(),
        Weather = Weather?.Normalize()
    };

    public (int Width, int Height) InternalSize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var pixels = Normalize().Quality switch
        {
            OceanQuality.Economy => 1280d * 720,
            OceanQuality.High => 3840d * 2160,
            _ => 1920d * 1080
        };
        var scale = Math.Min(1, Math.Min(Math.Sqrt(pixels / ((double)width * height)),
            8192d / Math.Max(width, height)));
        return (Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
    }
}
