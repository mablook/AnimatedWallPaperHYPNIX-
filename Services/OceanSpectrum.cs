using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class OceanSpectrum : IDisposable
{
    internal const float Choppiness = .45f;
    private readonly ID3D11DeviceContext _context;
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D11ComputeShader _evolve, _rows, _columns, _assemble;
    private readonly ID3D11Buffer _constants;
    private bool _disposed;
    public Band[] Bands { get; }
    private T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }

    internal sealed class Field : IDisposable
    {
        public ID3D11Texture2D Texture { get; }
        public ID3D11ShaderResourceView Read { get; }
        public ID3D11UnorderedAccessView Write { get; }
        public Field(ID3D11Device device, bool array)
        {
            try
            {
                Texture = device.CreateTexture2D(new Texture2DDescription(
                    array ? Format.R32G32B32A32_Float : Format.R16G16B16A16_Float,
                    256, 256, array ? 4u : 1u, array ? 1u : 0u,
                    BindFlags.ShaderResource | BindFlags.UnorderedAccess | (array ? BindFlags.None : BindFlags.RenderTarget),
                    miscFlags: array ? ResourceOptionFlags.None : ResourceOptionFlags.GenerateMips));
                Read = device.CreateShaderResourceView(Texture);
                Write = device.CreateUnorderedAccessView(Texture);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { Write?.Dispose(); Read?.Dispose(); Texture?.Dispose(); }
    }

    internal sealed class Band : IDisposable
    {
        private readonly List<IDisposable> _owned = [];
        public OceanSpectrumSeed Seed { get; }
        public ID3D11Texture2D Initial { get; }
        public ID3D11ShaderResourceView InitialRead { get; }
        public Field A { get; }
        public Field B { get; }
        public Field Displacement { get; }
        public Field DerivativesA { get; }
        public Field DerivativesB { get; }
        private double _epoch = double.NaN;
        private readonly Vector4[] _rotated = new Vector4[256 * 256];
        private T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }
        public Band(ID3D11Device device, OceanSpectrumSeed seed)
        {
            if (seed.Size != 256) throw new ArgumentException("GPU IFFT requires 256 samples per axis.", nameof(seed));
            Seed = seed;
            try
            {
                Initial = Own(device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float,
                    256, 512, 1, 1, BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write)));
                InitialRead = Own(device.CreateShaderResourceView(Initial));
                A = Own(new Field(device, true)); B = Own(new Field(device, true));
                Displacement = Own(new Field(device, false));
                DerivativesA = Own(new Field(device, false)); DerivativesB = Own(new Field(device, false));
            }
            catch { Dispose(); throw; }
        }

        public unsafe float UploadEpoch(ID3D11DeviceContext context, double time)
        {
            var epoch = Math.Floor(time / 64) * 64;
            if (epoch == _epoch) return (float)(time - epoch);
            Seed.WriteEpoch(_rotated, epoch);
            var mapped = context.Map(Initial, 0, MapMode.WriteDiscard);
            try
            {
                for (var y = 0; y < 256; y++)
                {
                    _rotated.AsSpan(y * 256, 256).CopyTo(new Span<Vector4>((void*)(mapped.DataPointer + y * (int)mapped.RowPitch), 256));
                    Seed.Geometry.AsSpan(y * 256, 256).CopyTo(new Span<Vector4>((void*)(mapped.DataPointer + (y + 256) * (int)mapped.RowPitch), 256));
                }
            }
            finally { context.Unmap(Initial, 0); }
            _epoch = epoch;
            return (float)(time - epoch);
        }
        public void Dispose() { for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose(); _owned.Clear(); }
    }

    public OceanSpectrum(ID3D11Device device, ID3D11DeviceContext context, OceanSpectrumSeed[]? seeds = null)
    {
        _context = context;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Shaders", "OceanSpectrum.hlsl");
            ID3D11ComputeShader Compile(string name) => Own(device.CreateComputeShader(Compiler.CompileFromFile(path, name, "cs_5_0").Span));
            _evolve = Compile("Evolve"); _rows = Compile("InverseRows");
            _columns = Compile("InverseColumns"); _assemble = Compile("Assemble");
            _constants = Own(device.CreateBuffer(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            seeds ??= [new(0), new(1), new(2)];
            Bands = seeds.Select(seed => Own(new Band(device, seed))).ToArray();
        }
        catch { Dispose(); throw; }
    }

    public unsafe void Update(double time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(time) || time < 0) time = 0;
        _context.CSSetConstantBuffer(0, _constants);
        foreach (var band in Bands)
        {
            var localTime = band.UploadEpoch(_context, time);
            var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
            try { *(Vector4*)mapped.DataPointer = new(localTime, 256, band.Seed.Length, 0); }
            finally { _context.Unmap(_constants, 0); }
            _context.CSSetShader(_evolve);
            _context.CSSetShaderResource(0, band.InitialRead);
            _context.CSSetUnorderedAccessView(0, band.A.Write);
            _context.Dispatch(32, 32, 1);
            ClearBindings();
            Transform(_rows, band.A, band.B);
            Transform(_columns, band.B, band.A);
            _context.CSSetShader(_assemble);
            _context.CSSetShaderResource(1, band.A.Read);
            _context.CSSetUnorderedAccessView(1, band.Displacement.Write);
            _context.CSSetUnorderedAccessView(2, band.DerivativesA.Write);
            _context.CSSetUnorderedAccessView(3, band.DerivativesB.Write);
            _context.Dispatch(32, 32, 1);
            ClearBindings();
            _context.GenerateMips(band.Displacement.Read);
            _context.GenerateMips(band.DerivativesA.Read);
            _context.GenerateMips(band.DerivativesB.Read);
        }
        _context.CSSetShader(null!);
    }

    private void Transform(ID3D11ComputeShader shader, Field source, Field target)
    {
        _context.CSSetShader(shader);
        _context.CSSetShaderResource(1, source.Read);
        _context.CSSetUnorderedAccessView(0, target.Write);
        _context.Dispatch(256, 4, 1);
        ClearBindings();
    }

    private void ClearBindings()
    {
        _context.CSSetShaderResource(0, null!); _context.CSSetShaderResource(1, null!);
        for (uint slot = 0; slot < 4; slot++) _context.CSSetUnorderedAccessView(slot, null!);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}
