using System.Numerics;

namespace AnimatedWallPaper.Services;

// Independent Gaussian travelling modes. The three power windows partition the
// wavelength domain; RMS is an explicit artistic sea-state calibration, not JONSWAP.
internal sealed class OceanSpectrumSeed
{
    internal const int Resolution = 256;
    public int Size { get; }
    public float Length { get; }
    public float TargetRms { get; }
    public Vector2[] Coefficients { get; }
    public Vector4[] Geometry { get; }

    public OceanSpectrumSeed(int band, int size = Resolution)
    {
        if (band is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(band));
        if (size < 4 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size;
        Length = band switch { 0 => 192, 1 => 28, _ => 4.5f };
        TargetRms = band switch { 0 => .24f, 1 => .12f, _ => .012f };
        Coefficients = new Vector2[size * size];
        Geometry = new Vector4[size * size];
        var random = new Random(6389 + band * 829);
        double power = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var ix = x < size / 2 ? x : x - size;
            var iy = y < size / 2 ? y : y - size;
            var k = new Vector2(ix, iy) * (2 * MathF.PI / Length);
            var magnitude = k.Length();
            var index = y * size + x;
            Geometry[index] = new(k, MathF.Sqrt(9.81f * magnitude), magnitude > 0 ? 1 / magnitude : 0);
            // Derivatives at a self-conjugate Nyquist bin must not create an imaginary field.
            if (magnitude == 0 || x == size / 2 || y == size / 2) continue;
            var wavelength = 2 * Math.PI / magnitude;
            var window = PowerWindows(wavelength)[band];
            var dotWind = (k.X * .32 + k.Y * .9474) / magnitude;
            var directional = (.28 + .72 * dotWind * dotWind) * (dotWind < 0 ? 1 : .18);
            var density = Math.Exp(-1 / Math.Pow(magnitude * 18, 2)) * directional * window /
                Math.Pow(magnitude, 4) * Math.Exp(-Math.Pow(2.0 / wavelength, 4) * (band == 0 ? 1 : 0));
            var radius = Math.Sqrt(-2 * Math.Log(Math.Max(1e-12, random.NextDouble())));
            var angle = random.NextDouble() * Math.PI * 2;
            var amplitude = Math.Sqrt(density * .5);
            var value = new Vector2((float)(radius * Math.Cos(angle) * amplitude),
                (float)(radius * Math.Sin(angle) * amplitude));
            Coefficients[index] = value;
            power += value.LengthSquared();
        }
        // Inverse FFT divides by N per axis; expected field variance is 2*sum(|h0|^2)/N^4.
        var scale = power > 0 ? TargetRms * size * size / Math.Sqrt(2 * power) : 0;
        for (var i = 0; i < Coefficients.Length; i++) Coefficients[i] *= (float)scale;
    }

    internal static Vector3 PowerWindows(double wavelength)
    {
        static double Smooth(double low, double high, double x)
        {
            var t = Math.Clamp((x - low) / (high - low), 0, 1);
            return t * t * (3 - 2 * t);
        }
        var large = Smooth(7, 11, wavelength);
        var small = 1 - Smooth(.55, .9, wavelength);
        return new((float)large, (float)((1 - large) * (1 - small)), (float)small);
    }

    public int Mirror(int index)
    {
        var x = index % Size; var y = index / Size;
        return ((Size - y) % Size) * Size + (Size - x) % Size;
    }

    internal Complex Evolve(int index, double time)
    {
        var a = Coefficients[index]; var b = Coefficients[Mirror(index)];
        var phase = Complex.FromPolarCoordinates(1, Geometry[index].Z * time);
        return new Complex(a.X, a.Y) * phase + new Complex(b.X, -b.Y) * Complex.Conjugate(phase);
    }

    // Rebase time in double every 64 s, so the GPU never takes sin of a huge float.
    public void WriteEpoch(Span<Vector4> destination, double epoch)
    {
        if (destination.Length < Size * Size) throw new ArgumentException("Seed storage is too small.", nameof(destination));
        for (var i = 0; i < destination.Length && i < Coefficients.Length; i++)
        {
            var a = Coefficients[i]; var b = Coefficients[Mirror(i)];
            var phase = Math.IEEERemainder(Geometry[i].Z * epoch, 2 * Math.PI);
            var c = Math.Cos(phase); var s = Math.Sin(phase);
            destination[i] = new((float)(a.X * c - a.Y * s), (float)(a.X * s + a.Y * c),
                (float)(b.X * c - b.Y * s), (float)(-b.X * s - b.Y * c));
        }
    }
}
