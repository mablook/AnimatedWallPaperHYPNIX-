using System.IO;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

// Two deterministic time samples. Publishing happens after UAV unbinding and mip generation.
// The same angular radiance/transmission field drives sky and water; no per-frame volume march.
internal sealed class OceanClouds : IDisposable
{
    internal sealed record Profile(int Width, int Height, int Steps, int ShadowSteps, double Hz);
    internal static Profile For(OceanQuality quality) => quality switch
    {
        OceanQuality.Economy => new(1024, 256, 20, 2, 1),
        OceanQuality.High => new(4096, 1024, 64, 6, 2),
        _ => new(2048, 512, 48, 4, 2)
    };
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11ComputeShader _shader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11UnorderedAccessView[] _write = new ID3D11UnorderedAccessView[2];
    private readonly ID3D11ShaderResourceView[] _read = new ID3D11ShaderResourceView[2];
    internal ID3D11Texture2D[] Textures { get; } = new ID3D11Texture2D[2];
    private readonly long[] _ticks = [long.MinValue, long.MinValue];
    private (OceanLighting, bool)? _key;
    private int _first;
    public OceanQuality Quality { get; }
    public Profile Budget { get; }
    public float Blend { get; private set; }
    public int BuildCount { get; private set; }
    public ID3D11ShaderResourceView Previous => _read[_first];
    public ID3D11ShaderResourceView Next => _read[1 - _first];
    public long EstimatedBytes => (long)(Budget.Width * Budget.Height * 8 * 2 * 4d / 3);
    private T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }
    public OceanClouds(ID3D11Device device, ID3D11DeviceContext context, OceanQuality quality)
    {
        _context = context; Quality = quality; Budget = For(quality);
        try
        {
            _shader = Own(device.CreateComputeShader(Compiler.CompileFromFile(Path.Combine(AppContext.BaseDirectory, "Shaders", "OceanClouds.hlsl"), "BuildClouds", "cs_5_0").Span));
            _constants = Own(device.CreateBuffer(80, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            for (var i = 0; i < 2; i++)
            {
                Textures[i] = Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,
                    (uint)Budget.Width, (uint)Budget.Height, 1, 0, BindFlags.ShaderResource | BindFlags.UnorderedAccess | BindFlags.RenderTarget,
                    miscFlags: ResourceOptionFlags.GenerateMips)));
                _read[i] = Own(device.CreateShaderResourceView(Textures[i]));
                _write[i] = Own(device.CreateUnorderedAccessView(Textures[i]));
            }
        }
        catch { Dispose(); throw; }
    }
    public void Update(double time, OceanSettings settings)
    {
        var key = (settings.Lighting, settings.Lighting == OceanLighting.Moon && settings.GibbousMoon);
        if (_key != key) { _ticks[0] = _ticks[1] = long.MinValue; _key = key; }
        var frame = Math.Max(0, double.IsFinite(time) ? time : 0) * Budget.Hz;
        var tick = (long)Math.Floor(frame);
        Blend = (float)(frame - tick);
        _first = _ticks[1] == tick ? 1 : _ticks[0] == tick ? 0 : _ticks[0] == tick + 1 ? 1 : 0;
        if (_ticks[_first] != tick) Build(_first, tick, settings);
        if (_ticks[1 - _first] != tick + 1) Build(1 - _first, tick + 1, settings);
    }
    private unsafe void Build(int index, long tick, OceanSettings settings)
    {
        var preset = OceanLightingModel.For(settings.Lighting, true);
        var phase = settings.Lighting == OceanLighting.Moon && settings.GibbousMoon ? OceanLightingModel.GibbousEnergy : 1;
        var energy = preset.Irradiance * phase;
        var transmission = OceanLightingModel.Transmittance(preset.Direction);
        var time = tick / Budget.Hz;
        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
        try
        {
            var data = new Span<Vector4>((void*)mapped.DataPointer, 5);
            data[0] = new(preset.Direction, preset.CloudCover);
            data[1] = new(energy * transmission, preset.Night ? 1 : 0);
            data[2] = new(Budget.Width, Budget.Height, Budget.Steps, Budget.ShadowSteps);
            // Periodic noise lattice avoids precision loss and has no discontinuity at wrapping.
            data[3] = new((float)(time * .009 % 10240), (float)(time * .003 % 10240), settings.Lighting == OceanLighting.Overcast ? 1 : 0, 0);
            data[4] = new(energy, 0);
        }
        finally { _context.Unmap(_constants, 0); }
        _context.CSSetShader(_shader); _context.CSSetConstantBuffer(0, _constants);
        _context.CSSetUnorderedAccessView(0, _write[index]);
        _context.Dispatch((uint)(Budget.Width / 8), (uint)(Budget.Height / 8), 1);
        _context.CSSetUnorderedAccessView(0, null!); _context.CSSetShader(null!);
        _context.GenerateMips(_read[index]); _ticks[index] = tick; BuildCount++;
    }
    public void Dispose() { for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose(); _owned.Clear(); }
}
