using System.Numerics;

namespace AnimatedWallPaper.Services;

// Analytic P1 comparison field, NOT an FFT/JONSWAP implementation. All quality
// levels share these components and phases; only spatial sampling changes.
internal static class OceanWaveModel
{
    public const int Count = 32;
    public const int GeometryCount = 16;
    public const float Choppiness = 0.65f;
    private const double Tau = Math.PI * 2;
    private static readonly Wave[] Waves = Build();
    internal readonly record struct Wave(Vector2 K, float BaseAmplitude, double Phase, double Omega);

    private static Wave[] Build()
    {
        var random = new Random(4173);
        var result = new Wave[Count];
        for (var i = 0; i < Count; i++)
        {
            var length = 35 * Math.Exp(-i * Math.Log(35 / .055) / (Count - 1));
            var k = Tau / length;
            var angle = .36 + (random.NextDouble() - .5) * (i < 6 ? 1.65 : 2.7);
            if (i % 7 == 2) angle -= 1.1; // crossed swell, no grid-aligned periodic pattern
            var slope = (.063 + random.NextDouble() * .027) * (i < 3 ? 1.12 : 1)
                * Math.Pow(Math.Min(1, length / .65), .4);
            result[i] = new Wave(new((float)(Math.Sin(angle) * k), (float)(Math.Cos(angle) * k)),
                (float)(slope / k), random.NextDouble() * Tau, Math.Sqrt(9.81 * k));
        }
        return result;
    }

    public static void Write(Span<Vector4> destination, double time, float agitation)
    {
        if (destination.Length < Count) throw new ArgumentException("32 wave vectors required.", nameof(destination));
        if (!double.IsFinite(time) || time < 0) time = 0;
        agitation = float.IsFinite(agitation) ? Math.Clamp(agitation, 0, 1) : .65f;
        for (var i = 0; i < Count; i++)
        {
            var wave = Waves[i];
            // Calm retains long swell but loses much more short wind-wave energy.
            var strength = i < 6 ? .43f + .69f * agitation : .20f + .90f * agitation;
            var phase = Math.IEEERemainder(wave.Phase - wave.Omega * time, Tau);
            destination[i] = new(wave.K, wave.BaseAmplitude * strength, (float)phase);
        }
    }

    // A small independent CPU reference is used to verify displacement derivatives.
    internal static (Vector3 Position, Vector3 Dx, Vector3 Dz) Evaluate(Vector2 point, double time, float agitation)
    {
        Span<Vector4> waves = stackalloc Vector4[Count];
        Write(waves, time, agitation);
        var p = new Vector3(point.X, 0, point.Y);
        var dx = Vector3.UnitX;
        var dz = Vector3.UnitZ;
        for (var i = 0; i < GeometryCount; i++)
        {
            var w = waves[i];
            var k = new Vector2(w.X, w.Y);
            var direction = Vector2.Normalize(k);
            var phase = Vector2.Dot(point, k) + w.W;
            var sine = MathF.Sin(phase);
            var cosine = MathF.Cos(phase);
            p += new Vector3(-Choppiness * w.Z * direction.X * cosine, w.Z * sine,
                -Choppiness * w.Z * direction.Y * cosine);
            var derivative = new Vector3(Choppiness * w.Z * direction.X * sine, w.Z * cosine,
                Choppiness * w.Z * direction.Y * sine);
            dx += derivative * k.X;
            dz += derivative * k.Y;
        }
        return (p, dx, dz);
    }
}
