using System.Numerics;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class OceanTests
{
    [Fact]
    public void AtmosphereTransmittanceIsBoundedAndReddenedNearTheHorizon()
    {
        var high = OceanLightingModel.Transmittance(Vector3.UnitY);
        var low = OceanLightingModel.Transmittance(Vector3.Normalize(new Vector3(0,.075f,1)));
        foreach (var value in new[] { high.X,high.Y,high.Z,low.X,low.Y,low.Z }) Assert.InRange(value,0,1);
        Assert.True(low.X < high.X && low.Y < high.Y && low.Z < high.Z);
        Assert.True(low.Z/low.X < high.Z/high.X);
    }

    [Fact]
    public void DirectDiskAndScatteringShareAnIlluminant()
    {
        foreach (var light in new[] { OceanLighting.Day,OceanLighting.Sunset,OceanLighting.Moon })
        {
            var preset = OceanLightingModel.For(light,true);
            var direct = preset.Irradiance * OceanLightingModel.Transmittance(preset.Direction);
            var diskIntegral = new Vector3(preset.Radiance.X,preset.Radiance.Y,preset.Radiance.Z) *
                (MathF.PI*OceanLightingModel.AngularRadius*OceanLightingModel.AngularRadius);
            Assert.True(Vector3.Distance(direct,diskIntegral) < 1e-5);
        }
        var overcast = OceanLightingModel.For(OceanLighting.Overcast,true);
        Assert.Equal(Vector3.Zero,new Vector3(overcast.Radiance.X,overcast.Radiance.Y,overcast.Radiance.Z));
    }

    [Fact]
    public void SpectralPowerWindowsPartitionTheWavelengthDomain()
    {
        for (var i = 0; i <= 1000; i++)
        {
            var windows = OceanSpectrumSeed.PowerWindows(Math.Pow(10, -3 + i * .007));
            Assert.InRange(windows.X, 0, 1); Assert.InRange(windows.Y, 0, 1); Assert.InRange(windows.Z, 0, 1);
            Assert.InRange(Math.Abs(windows.X + windows.Y + windows.Z - 1), 0, 1e-6);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void SpectraAreDeterministicHermitianAndHaveCalibratedExpectedVariance(int band)
    {
        var seed = new OceanSpectrumSeed(band);
        var repeated = new OceanSpectrumSeed(band);
        Assert.Equal(seed.Coefficients, repeated.Coefficients);
        var variance = 2 * seed.Coefficients.Sum(v => (double)v.X * v.X + (double)v.Y * v.Y) / Math.Pow(seed.Size, 4);
        Assert.InRange(Math.Abs(variance - seed.TargetRms * seed.TargetRms), 0, 1e-8);
        for (var i = 0; i < seed.Size; i++)
        {
            Assert.Equal(Vector2.Zero, seed.Coefficients[i * seed.Size + seed.Size / 2]);
            Assert.Equal(Vector2.Zero, seed.Coefficients[seed.Size / 2 * seed.Size + i]);
        }
        foreach (var time in new[] { 0d, 63.999, 64, 36000.125 })
        for (var i = 0; i < seed.Coefficients.Length; i += 43)
        {
            Assert.Equal(i, seed.Mirror(seed.Mirror(i)));
            Assert.InRange(Complex.Abs(seed.Evolve(i, time) - Complex.Conjugate(seed.Evolve(seed.Mirror(i), time))), 0, 1e-7);
        }
    }

    [Fact]
    public void RebasingSpectralTimePreservesPhaseAfterThirtyDays()
    {
        var seed = new OceanSpectrumSeed(2);
        var rotated = new Vector4[seed.Size * seed.Size];
        const double time = 30 * 24 * 3600 + 13.375;
        var epoch = Math.Floor(time / 64) * 64;
        seed.WriteEpoch(rotated, epoch);
        for (var i = 0; i < rotated.Length; i += 43)
        {
            var value = rotated[i];
            var phase = Complex.FromPolarCoordinates(1, seed.Geometry[i].Z * (time - epoch));
            var rebased = new Complex(value.X, value.Y) * phase + new Complex(value.Z, value.W) * Complex.Conjugate(phase);
            Assert.InRange(Complex.Abs(rebased - seed.Evolve(i, time)), 0, 2e-5);
        }
    }

    [Theory]
    [InlineData(15)] [InlineData(30)] [InlineData(60)]
    public void ClockPreservesActiveTimeAcrossPauseAndSpeedChanges(int fps)
    {
        var clock = new OceanFrameClock();
        clock.Advance(0);
        for (var i = 1; i <= fps * 3; i++) clock.Advance(i / (double)fps);
        clock.SetPaused(true, 3);
        Assert.Equal(3, clock.Advance(103), 8);
        clock.SetPaused(false, 103);
        Assert.Equal(3.1, clock.Advance(103.1), 8);
        clock.SetSpeed(.5, 103.1);
        Assert.Equal(4.1, clock.Advance(105.1), 8);
        Assert.Equal(4.1, clock.Advance(double.NaN), 8);
        Assert.Equal(4.1, clock.Advance(100), 8);
    }

    [Fact]
    public void PausedOutputDoesNotStopAnotherOutput()
    {
        var a = new OceanFrameClock(); var b = new OceanFrameClock();
        a.Advance(0); b.Advance(0);
        a.SetPaused(true, 2);
        Assert.Equal(2, a.Advance(60));
        Assert.Equal(60, b.Advance(60));
    }

    [Fact]
    public void FullDisplacementDerivativesAgreeWithFiniteDifferences()
    {
        const float step = .002f;
        foreach (var strength in new[] { 0f, .65f, 1f })
        foreach (var time in new[] { 0d, 10d, 36000d })
        {
            var q = new Vector2(1.21f, -2.36f);
            var field = OceanWaveModel.Evaluate(q, time, strength);
            var dx = (OceanWaveModel.Evaluate(q + Vector2.UnitX * step, time, strength).Position -
                OceanWaveModel.Evaluate(q - Vector2.UnitX * step, time, strength).Position) / (2 * step);
            var dz = (OceanWaveModel.Evaluate(q + Vector2.UnitY * step, time, strength).Position -
                OceanWaveModel.Evaluate(q - Vector2.UnitY * step, time, strength).Position) / (2 * step);
            Assert.True(Vector3.Distance(dx, field.Dx) < .002, $"dx derivative mismatch: {dx} / {field.Dx}");
            Assert.True(Vector3.Distance(dz, field.Dz) < .002);
            Assert.True(Vector3.Cross(field.Dz, field.Dx).Y > 0);
        }
    }

    [Fact]
    public void BoundedSteepnessPreventsFoldingAndLongRunningPhasesStayFinite()
    {
        var modes = new Vector4[OceanWaveModel.Count];
        OceanWaveModel.Write(modes, 60 * 60 * 24 * 30d, 1);
        var bound = modes.Take(OceanWaveModel.GeometryCount)
            .Sum(w => w.Z * new Vector2(w.X, w.Y).Length() * OceanWaveModel.Choppiness);
        Assert.True(bound < 1, $"Horizontal Jacobian bound is unsafe: {bound}");
        Assert.All(modes, w => Assert.InRange(w.W, -MathF.PI, MathF.PI));
        OceanWaveModel.Write(modes, double.NaN, float.NaN);
        Assert.All(modes, w => Assert.True(float.IsFinite(w.Z) && float.IsFinite(w.W)));
    }

    [Theory]
    [InlineData(3840, 2160)] [InlineData(1080, 1920)] [InlineData(5120, 1440)]
    public void QualityPreservesAspectAndSettingsRejectInvalidNumbers(int width, int height)
    {
        var settings = new OceanSettings(Agitation: float.NaN, Speed: float.PositiveInfinity);
        Assert.Equal(.65f, settings.Normalize().Agitation);
        Assert.Equal(1, settings.Normalize().Speed);
        foreach (var quality in Enum.GetValues<OceanQuality>())
        {
            var size = (settings with { Quality = quality }).InternalSize(width, height);
            Assert.True(size.Width <= width && size.Height <= height);
            Assert.True(Math.Abs(size.Width / (double)size.Height - width / (double)height) < .01);
        }
    }
}
