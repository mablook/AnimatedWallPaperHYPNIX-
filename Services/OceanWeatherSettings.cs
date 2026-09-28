using System.Numerics;

namespace AnimatedWallPaper.Services;

internal enum OceanCloudType { Stratocumulus, Cumulus, Stratus }
internal enum OceanFog { Clear, Maritime, LowMist, Banks }

// Rendering parameters, not a weather forecast. Lengths below are kilometres.
internal sealed record OceanWeatherSettings(
    OceanCloudType Clouds = OceanCloudType.Stratocumulus,
    OceanFog Fog = OceanFog.Maritime,
    float Coverage = .25f,
    float WindMetresPerSecond = 9,
    int Seed = 27)
{
    public OceanWeatherSettings Normalize() => this with
    {
        Clouds = Enum.IsDefined(Clouds) ? Clouds : OceanCloudType.Stratocumulus,
        Fog = Enum.IsDefined(Fog) ? Fog : OceanFog.Maritime,
        Coverage = float.IsFinite(Coverage) ? Math.Clamp(Coverage, 0, 1) : .25f,
        WindMetresPerSecond = float.IsFinite(WindMetresPerSecond) ? Math.Clamp(WindMetresPerSecond, 0, 30) : 9,
        Seed = Math.Clamp(Seed, 0, 65535)
    };
    public (float Base, float Top) Layer => Clouds switch
    {
        OceanCloudType.Cumulus => (1.1f, 3.1f),
        OceanCloudType.Stratus => (.65f, 1.25f),
        _ => (1.0f, 2.1f)
    };
    // Extra droplet extinction, separate from the existing RGB atmosphere.
    public (float Extinction, float Height, float Banks) FogProfile => Fog switch
    {
        OceanFog.Clear => (0, .12f, 0),
        OceanFog.LowMist => (.60f, .085f, 0),
        OceanFog.Banks => (1.8f, .065f, 1),
        _ => (.065f, .18f, 0)
    };
}

internal static class OceanVolumeMath
{
    public static double Transmission(double extinction, double distance) => Math.Exp(-Math.Max(0, extinction) * Math.Max(0, distance));
    public static double SegmentWeight(double extinction, double distance)
    {
        var x = extinction * distance;
        return Math.Abs(x) < 1e-5 ? distance * (1 - x / 2 + x * x / 6) : (1 - Math.Exp(-x)) / extinction;
    }
    public static double Phase(double mu, double g) => (1 - g*g) / (4 * Math.PI * Math.Pow(1 + g*g - 2*g*mu, 1.5));
    public static (Vector3 S, Vector3 T) Compose((Vector3 S, Vector3 T) near, (Vector3 S, Vector3 T) far) =>
        (near.S + near.T * far.S, near.T * far.T);
}
