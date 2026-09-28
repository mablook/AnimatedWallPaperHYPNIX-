using System.IO;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

// Directional reflectance of the same single-scattering Beckmann/Smith BRDF used
// for the direct Sun and Moon. Built once per device; independent of the sky,
// weather, waves and celestial time. RG stores A/B, not a display-space color.
internal sealed class OceanReflectionLut : IDisposable
{
    public const int Resolution = 128;
    public const int SampleCount = 1024;
    public const long EstimatedBytes = Resolution * Resolution * 4;
    private readonly List<IDisposable> _owned = [];
    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView Read { get; }
    private T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }

    // Grid vertices: NoV = (x/127)^2, alpha^2 = (y/127)^2.
    // Sample with a linear-clamp sampler at
    // uv = (sqrt(saturate(float2(NoV, alphaSquared))) * 127 + .5) / 128.
    // This places more samples near grazing angles and small slope variance.
    // The returned single-scattering reflectance is F0*A + (1-F0)*B.
    // Values above alpha^2=1 use the endpoint; this is the LUT's supported domain.
    public OceanReflectionLut(ID3D11Device device, ID3D11DeviceContext context)
    {
        try
        {
            Texture = Own(device.CreateTexture2D(new Texture2DDescription(
                Format.R16G16_Float, Resolution, Resolution, 1, 1,
                BindFlags.ShaderResource | BindFlags.UnorderedAccess)));
            Read = Own(device.CreateShaderResourceView(Texture));
            using var write = device.CreateUnorderedAccessView(Texture);
            using var shader = device.CreateComputeShader(OceanShader.Compile(
                Path.Combine(AppContext.BaseDirectory, "Shaders", "OceanReflectionLut.hlsl"),
                "BuildReflectionLut", "cs_5_0").Span);
            try
            {
                context.CSSetShader(shader);
                context.CSSetUnorderedAccessView(0, write);
                context.Dispatch(Resolution / 8, Resolution / 8, 1);
            }
            finally
            {
                context.CSSetUnorderedAccessView(0, null!);
                context.CSSetShader(null!);
            }
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}
