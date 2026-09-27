using System.Numerics;
using System.Runtime.InteropServices;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class OceanSpectrumChecks
{
    // Independent direct DFT reference: never uses the GPU FFT's butterfly/order.
    private static double[] Reference(OceanSpectrumSeed seed, double time, int x, int y)
    {
        var result = new double[8];
        for (var i = 0; i < seed.Coefficients.Length; i++)
        {
            var ix = i % seed.Size; var iy = i / seed.Size;
            if (ix >= seed.Size / 2) ix -= seed.Size;
            if (iy >= seed.Size / 2) iy -= seed.Size;
            var h = seed.Evolve(i, time) * Complex.FromPolarCoordinates(1,
                2 * Math.PI * (ix * x + iy * y) / seed.Size);
            var k = seed.Geometry[i];
            var real = h.Real / (seed.Size * seed.Size);
            var ih = -h.Imaginary / (seed.Size * seed.Size);
            result[0] += real; result[1] += ih * k.X * k.W;
            result[2] += ih * k.Y * k.W; result[3] += ih * k.X;
            result[4] += ih * k.Y; result[5] -= real * k.X * k.X * k.W;
            result[6] -= real * k.X * k.Y * k.W; result[7] -= real * k.Y * k.Y * k.W;
        }
        return result;
    }

    internal static float[][] ReadFields(ID3D11Device device, ID3D11DeviceContext context, OceanSpectrum.Band band)
    {
        var description = band.A.Texture.Description;
        description.BindFlags = BindFlags.None; description.MiscFlags = ResourceOptionFlags.None;
        description.Usage = ResourceUsage.Staging; description.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = device.CreateTexture2D(description);
        context.CopyResource(staging, band.A.Texture);
        var result = new float[4][];
        for (uint slice = 0; slice < 4; slice++)
        {
            result[slice] = new float[256 * 256 * 4];
            var mapped = context.Map(staging, slice, MapMode.Read);
            try
            {
                for (var y = 0; y < 256; y++)
                    Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, result[slice], y * 256 * 4, 256 * 4);
            }
            finally { context.Unmap(staging, slice); }
        }
        return result;
    }

    private static double ReadLastSlopeMoment(ID3D11Device device, ID3D11DeviceContext context, OceanSpectrum.Band band)
    {
        var description = band.DerivativesB.Texture.Description;
        description.BindFlags = BindFlags.None; description.MiscFlags = ResourceOptionFlags.None;
        description.Usage = ResourceUsage.Staging; description.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = device.CreateTexture2D(description);
        context.CopyResource(staging, band.DerivativesB.Texture);
        var mip = description.MipLevels - 1;
        var mapped = context.Map(staging, mip, MapMode.Read);
        try
        {
            return (double)BitConverter.UInt16BitsToHalf((ushort)Marshal.ReadInt16(mapped.DataPointer, 4)) +
                (double)BitConverter.UInt16BitsToHalf((ushort)Marshal.ReadInt16(mapped.DataPointer, 6));
        }
        finally { context.Unmap(staging, mip); }
    }

    public static object Run(ID3D11Device device, ID3D11DeviceContext context)
    {
        double maxError = 0, maxImaginary = 0, maxEnergyError = 0, maxMomentError = 0;
        var comparisons = 0;
        foreach (var single in new[] { true, false })
        {
            var seed = new OceanSpectrumSeed(1);
            if (single)
            {
                Array.Clear(seed.Coefficients);
                seed.Coefficients[2 * 256 + 3] = new(256 * 256 * .5f, 0);
            }
            using var spectrum = new OceanSpectrum(device, context, [seed]);
            foreach (var time in new[] { .37, 63.999, 64.0, 64.001, 36000.125 })
            {
                spectrum.Update(time);
                var fields = ReadFields(device, context, spectrum.Bands[0]);
                foreach (var (x, y) in new[] { (0, 0), (19, 71), (128, 128), (255, 253) })
                {
                    var reference = Reference(seed, time, x, y);
                    for (var component = 0; component < 8; component++)
                    {
                        var offset = (y * 256 + x) * 4 + (component % 2) * 2;
                        maxError = Math.Max(maxError, Math.Abs(fields[component / 2][offset] - reference[component]));
                        comparisons++;
                    }
                }
                double spatialEnergy = 0, slopeEnergy = 0, frequencyEnergy = 0;
                for (var i = 0; i < 256 * 256; i++)
                {
                    spatialEnergy += fields[0][i * 4] * (double)fields[0][i * 4];
                    slopeEnergy += fields[1][i * 4 + 2] * (double)fields[1][i * 4 + 2] +
                        fields[2][i * 4] * (double)fields[2][i * 4];
                    var h = seed.Evolve(i, time); frequencyEnergy += h.Real * h.Real + h.Imaginary * h.Imaginary;
                    foreach (var field in fields)
                    {
                        maxImaginary = Math.Max(maxImaginary, Math.Abs(field[i * 4 + 1]));
                        maxImaginary = Math.Max(maxImaginary, Math.Abs(field[i * 4 + 3]));
                    }
                }
                spatialEnergy /= 256 * 256;
                frequencyEnergy /= Math.Pow(256, 4);
                maxEnergyError = Math.Max(maxEnergyError, Math.Abs(spatialEnergy - frequencyEnergy));
                var mipMoment = ReadLastSlopeMoment(device, context, spectrum.Bands[0]);
                maxMomentError = Math.Max(maxMomentError, Math.Abs(mipMoment - slopeEnergy / (256 * 256)));
            }
        }
        double sampledMinimumHorizontalEigenvalueBound = 1;
        using (var ocean = new OceanSpectrum(device, context))
        {
            foreach (var time in new[] { 0d, 10, 64, 36000.125 })
            {
                ocean.Update(time);
                double compressionBound = 0;
                for (var band = 0; band < 3; band++)
                {
                    var fields = ReadFields(device, context, ocean.Bands[band]);
                    double minimumEigenvalue = 0;
                    for (var i = 0; i < 256 * 256; i++)
                    {
                        var xx = fields[2][4 * i + 2]; var xz = fields[3][4 * i]; var zz = fields[3][4 * i + 2];
                        var eigenvalue = .5 * (xx + zz - Math.Sqrt((xx - zz) * (xx - zz) + 4 * xz * xz));
                        minimumEigenvalue = Math.Min(minimumEigenvalue, eigenvalue);
                    }
                    compressionBound += minimumEigenvalue * (band == 1 ? 1.17 : 1.1) * OceanSpectrum.Choppiness;
                }
                sampledMinimumHorizontalEigenvalueBound = Math.Min(sampledMinimumHorizontalEigenvalueBound, 1 + compressionBound);
            }
        }
        if (sampledMinimumHorizontalEigenvalueBound <= .05)
            throw new Exception($"Ocean horizontal compression has insufficient margin in sampled states: {sampledMinimumHorizontalEigenvalueBound}.");
        if (!double.IsFinite(maxError) || maxError > .0001 || maxImaginary > .0001 || maxEnergyError > .00001 || maxMomentError > .001)
            throw new Exception($"Ocean IFFT mismatch: DFT={maxError}, imaginary={maxImaginary}, energy={maxEnergyError}, mipMoment={maxMomentError}.");
        return new { comparisons, maxError, maxImaginary, maxEnergyError, maxMomentError, sampledMinimumHorizontalEigenvalueBound,
            reference = "Independent double direct DFT; single travelling mode and random spectrum; eight fields; 64-second epoch boundaries and 10 hours." };
    }
}
