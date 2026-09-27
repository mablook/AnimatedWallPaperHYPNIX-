using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class OceanLightingChecks
{
    private static byte[] SurfaceHash(OceanGpuRenderer renderer)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var band in renderer.Spectrum!.Bands)
        foreach (var field in OceanSpectrumChecks.ReadFields(renderer.Device, renderer.Context, band))
            hash.AppendData(MemoryMarshal.AsBytes(field.AsSpan()));
        return hash.GetHashAndReset();
    }

    internal static object Run(OceanGpuRenderer renderer)
    {
        var settings = new OceanSettings();
        renderer.Render(10, settings);
        var surface = SurfaceHash(renderer);
        var first = renderer.Pixels();
        var buildCount = renderer.Atmosphere!.BuildCount;
        renderer.Render(11, settings);
        renderer.Render(10, settings with { Horizon = false, Quality = OceanQuality.Economy });
        if (renderer.Atmosphere.BuildCount != buildCount) throw new Exception("Sky cache rebuilt on an ordinary frame or camera change.");

        double maxSky = 0;
        foreach (var lighting in Enum.GetValues<OceanLighting>())
        {
            renderer.Render(10, settings with { Lighting = lighting });
            if (!surface.SequenceEqual(SurfaceHash(renderer))) throw new Exception("Lighting changed the approved wave field.");
            var texture = renderer.Atmosphere.Texture;
            var description = texture.Description;
            description.BindFlags = BindFlags.None; description.MiscFlags = ResourceOptionFlags.None;
            description.Usage = ResourceUsage.Staging; description.CPUAccessFlags = CpuAccessFlags.Read;
            using var staging = renderer.Device.CreateTexture2D(description);
            renderer.Context.CopyResource(staging, texture);
            var mapped = renderer.Context.Map(staging, 0, MapMode.Read);
            try
            {
                for (var y = 0; y < description.Height; y++)
                for (var x = 0; x < description.Width; x++)
                for (var component = 0; component < 4; component++)
                {
                    var value = (float)BitConverter.UInt16BitsToHalf((ushort)Marshal.ReadInt16(mapped.DataPointer,
                        y * (int)mapped.RowPitch + x * 8 + component * 2));
                    if (!float.IsFinite(value) || value < 0 || (component == 3 && value > 1))
                        throw new Exception($"Invalid atmosphere texel: {lighting} / {value}.");
                    if (component < 3) maxSky = Math.Max(maxSky, value);
                }
            }
            finally { renderer.Context.Unmap(staging, 0); }
        }
        renderer.Render(10, settings with { Atmosphere = false });
        if (!surface.SequenceEqual(SurfaceHash(renderer))) throw new Exception("Legacy light comparison changed the wave field.");
        renderer.Render(10, settings);
        if (!first.SequenceEqual(renderer.Pixels())) throw new Exception("Returning to a sky preset changed its image.");
        return new { surfaceFieldsIdenticalAcrossAllLights = true, cachedBetweenFrames = true,
            allSkyTexelsFinite = true, transmissionInRange = true, presetReturnDeterministic = true, maxSky };
    }
}
