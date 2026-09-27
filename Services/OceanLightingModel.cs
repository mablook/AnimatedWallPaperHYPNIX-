using System.Numerics;

namespace AnimatedWallPaper.Services;

// Pre-exposed RGB lighting, not absolute photometric units. Surface parameters live elsewhere.
internal static class OceanLightingModel
{
    internal sealed record Preset(Vector3 Direction, Vector4 Radiance, Vector4 Top,
        Vector4 Horizon, Vector3 Water, Vector3 Irradiance, float CloudCover, bool Night);
    internal const float AngularRadius = .00465f;
    // Perceptual art direction requested for low celestial bodies. The physical source
    // radius/irradiance remain separate so the approved water lighting does not change.
    internal static float ApparentRadius(float elevation, bool magnify)
    {
        if (!magnify || !float.IsFinite(elevation)) return AngularRadius;
        var t = Math.Clamp(elevation / (25 * MathF.PI / 180), 0, 1);
        var horizonWeight = 1 - t * t * (3 - 2 * t);
        return AngularRadius * (1 + 2.2f * horizonWeight);
    }
    // Integrated disk phase response of the shader's Lommel-Seeliger/Lambert blend, alpha=.85 rad.
    internal const float GibbousEnergy = .714779f;
    internal static readonly Vector3 Rayleigh = new(.0058f, .0135f, .0331f); // per km
    internal const float MieExtinction = .0044f;
    internal static readonly Vector3 Ozone = new(.00065f,.001881f,.000085f); // RGB approximation per km

    internal static Vector3 Transmittance(Vector3 direction)
    {
        const double radius = 6360, top = 6460, start = radius + .002;
        var mu = direction.Y;
        var distance = -start * mu + Math.Sqrt(start * start * (mu * mu - 1) + top * top);
        double rayleigh = 0, mie = 0, ozone = 0;
        // Quadratic intervals resolve dense air close to the observer, even at the horizon.
        for (var i = 0; i < 64; i++)
        {
            var a = distance * Math.Pow(i / 64d, 2); var b = distance * Math.Pow((i + 1) / 64d, 2);
            var t = (a + b) * .5;
            var altitude = Math.Max(0, Math.Sqrt(start * start + t * t + 2 * start * t * mu) - radius);
            rayleigh += Math.Exp(-altitude / 8) * (b - a);
            mie += Math.Exp(-altitude / 1.2) * (b - a);
            ozone += Math.Max(0,1-Math.Abs(altitude-25)/15) * (b-a);
        }
        return new((float)Math.Exp(-Rayleigh.X * rayleigh - MieExtinction * mie - Ozone.X*ozone),
            (float)Math.Exp(-Rayleigh.Y * rayleigh - MieExtinction * mie - Ozone.Y*ozone),
            (float)Math.Exp(-Rayleigh.Z * rayleigh - MieExtinction * mie - Ozone.Z*ozone));
    }

    private static readonly Preset[] Current = Enum.GetValues<OceanLighting>().Select(light => Create(light,true)).ToArray();
    private static readonly Preset[] Previous = Enum.GetValues<OceanLighting>().Select(light => Create(light,false)).ToArray();

    internal static Preset For(OceanLighting light, bool atmosphere) => (atmosphere ? Current : Previous)[(int)light];

    private static Preset Create(OceanLighting light, bool atmosphere)
    {
        var direction = Vector3.Normalize(light switch
        {
            OceanLighting.Day => new(.18f, .52f, .84f),
            OceanLighting.Moon => new(.08f, atmosphere ? .145f : .10f, 1),
            _ => new Vector3(.10f, .075f, 1)
        });
        if (!atmosphere)
        {
            var old = light switch
            {
                OceanLighting.Day => new Preset(direction, new(16000,15400,14000,1.2f), new(.06f,.16f,.30f,0), new(.42f,.52f,.61f,0), new(.005f,.018f,.022f), default,0,false),
                OceanLighting.Moon => new Preset(direction, new(4500,5000,5800,.85f), new(.002f,.005f,.014f,0), new(.035f,.045f,.065f,0), new(.001f,.003f,.005f), default,0,true),
                OceanLighting.Overcast => new Preset(direction, new(0,0,0,1), new(.06f,.075f,.09f,0), new(.32f,.35f,.37f,0), new(.004f,.012f,.013f), default,0,false),
                _ => new Preset(direction, new(23000,12600,4200,1.15f), new(.025f,.052f,.10f,0), new(.36f,.225f,.12f,0), new(.003f,.009f,.012f), default,0,false)
            };
            return old;
        }
        var irradiance = light switch
        {
            OceanLighting.Day => new Vector3(6.2f,6.0f,5.8f),
            OceanLighting.Moon => new Vector3(.40f,.48f,.65f),
            OceanLighting.Overcast => new Vector3(3.2f),
            _ => new Vector3(4.0f,3.9f,3.8f)
        };
        // Artistic night white balance is applied to the illuminant before both
        // scattering and reflection, never as a blue overlay on the final image.
        if (light == OceanLighting.Moon)
            irradiance = new Vector3(3200,3550,4000) * (MathF.PI*AngularRadius*AngularRadius) / Transmittance(direction);
        var radiance = irradiance * Transmittance(direction) / (MathF.PI * AngularRadius * AngularRadius);
        if (light == OceanLighting.Overcast) radiance = Vector3.Zero;
        var water = light switch
        {
            OceanLighting.Moon => new Vector3(.0006f,.0016f,.0023f),
            OceanLighting.Day => new Vector3(.0025f,.009f,.014f),
            _ => new Vector3(.0018f,.005f,.008f)
        };
        return new(direction, new(radiance, light == OceanLighting.Moon ? .85f : 1.1f),
            new(0,0,0,1), default, water, irradiance,
            light switch { OceanLighting.Day => .34f, OceanLighting.Moon => .20f, OceanLighting.Overcast => .92f, _ => .44f },
            light == OceanLighting.Moon);
    }
}
