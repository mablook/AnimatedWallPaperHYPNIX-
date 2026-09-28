using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace AnimatedWallPaper.Services;

internal enum OceanRenderDiagnostic { None, SunDirect, MoonDirect, ReflectedEnvironment, AerialScatter }

// One ocean surface per logical output. Its owner supplies active time, including after
// recreation. Preview uses the existing offscreen/GDI path; desktop uses DXGI.
internal sealed class OceanGpuRenderer : IDisposable
{
    private const int Columns = 384, Rows = 256;
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1? _swap;
    private readonly GpuPreviewSurface? _preview;
    private readonly IntPtr _window;
    private readonly ID3D11Texture2D _back;
    private readonly ID3D11RenderTargetView _target;
    private readonly ID3D11VertexShader _screenVs, _waterVs;
    private readonly ID3D11PixelShader _skyPs, _waterPs, _compositePs;
    private readonly ID3D11Buffer _constants, _indices;
    private readonly ID3D11DepthStencilState _depthOn, _depthOff;
    private readonly ID3D11RasterizerState _rasterizer;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11SamplerState _surfaceSampler;
    private OceanSpectrum? _spectrum;
    private OceanAtmosphere? _atmosphere;
    private OceanMoonTexture? _moon;
    private OceanClouds? _clouds;
    private OceanBloom? _bloom;
    private OceanOpticalDepth? _opticalDepth;
    private OceanCycleSky? _cycleSky;
    private OceanVolumetrics? _volumes;
    private OceanReflectionLut? _reflectionLut;
    internal OceanReflectionLut? ReflectionLut => _reflectionLut;
    internal OceanVolumetrics? Volumes => _volumes;
    internal OceanCelestialFrame? CelestialFrame { get; private set; }
    internal OceanRenderDiagnostic Diagnostic { get; set; }
    internal bool FlatDiagnosticSurface { get; set; }
    internal ID3D11Texture2D HdrScene => _scene!;
    internal OceanCycleSky? CycleSky => _cycleSky;
    private readonly List<IDisposable> _sceneResources = [];
    private ID3D11Texture2D? _scene;
    private ID3D11RenderTargetView? _sceneTarget;
    private ID3D11ShaderResourceView? _sceneRead;
    private ID3D11DepthStencilView? _depth;
    private bool _disposed;
    public int Width { get; }
    public int Height { get; }
    public int InternalWidth { get; private set; }
    public int InternalHeight { get; private set; }
    public string AdapterName { get; }
    internal ID3D11Device Device => _device;
    internal ID3D11DeviceContext Context => _context;
    internal GpuPreviewSurface? PreviewSurface => _preview;
    public long EstimatedTextureBytes => (long)InternalWidth * InternalHeight * 12 + (long)Width * Height * 8
        + (_spectrum is null ? 0 : 3L * (256 * 512 * 16 + 2 * 4 * 256 * 256 * 16 + 3 * 87381 * 8))
        + (_atmosphere is null ? 0 : 1398103L * 8)
        + (_moon is null ? 0 : OceanMoonTexture.EstimatedBytes) + (_clouds?.EstimatedBytes ?? 0) + (_bloom?.EstimatedBytes ?? 0)
        + (_cycleSky?.EstimatedBytes ?? 0) + (_volumes?.EstimatedBytes ?? 0) + (_reflectionLut is null ? 0 : OceanReflectionLut.EstimatedBytes)
        + (_opticalDepth is null ? 0 : OceanOpticalDepth.Width*OceanOpticalDepth.Height*16);
    internal OceanSpectrum? Spectrum => _spectrum;
    internal OceanAtmosphere? Atmosphere => _atmosphere;
    internal OceanClouds? Clouds => _clouds;

    private T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }
    private T SceneOwn<T>(T resource) where T : IDisposable { _sceneResources.Add(resource); return resource; }

    public OceanGpuRenderer(IntPtr hwnd, int width, int height, bool preview = true)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; _window = hwnd;
        try
        {
            var factory = Own(CreateDXGIFactory1<IDXGIFactory2>());
            D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_0], out _device, out _, out _context).CheckError();
            Own(_device); Own(_context);
            using (var dxgi = _device.QueryInterface<IDXGIDevice>())
            using (var adapter = dxgi.GetAdapter()) AdapterName = adapter.Description.Description;
            if (preview)
            {
                _preview = Own(new GpuPreviewSurface(_device, _context, width, height));
                _back = _preview.Target;
            }
            else
            {
                _swap = Own(factory.CreateSwapChainForHwnd(_device, hwnd, new SwapChainDescription1
                {
                    Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm,
                    BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
                    SampleDescription = SampleDescription.Default, Scaling = Scaling.Stretch,
                    SwapEffect = SwapEffect.FlipDiscard, AlphaMode = AlphaMode.Ignore
                }, new SwapChainFullscreenDescription { Windowed = true }));
                factory.MakeWindowAssociation(hwnd, (WindowAssociationFlags)0x3);
                _back = Own(_swap.GetBuffer<ID3D11Texture2D>(0));
            }
            _target = Own(_device.CreateRenderTargetView(_back));
            var path = Path.Combine(AppContext.BaseDirectory, "Shaders", "Ocean.hlsl");
            _screenVs = Own(_device.CreateVertexShader(OceanShader.Compile(path, "ScreenVS", "vs_5_0").Span));
            _waterVs = Own(_device.CreateVertexShader(OceanShader.Compile(path, "WaterVS", "vs_5_0").Span));
            _skyPs = Own(_device.CreatePixelShader(OceanShader.Compile(path, "SkyPS", "ps_5_0").Span));
            _waterPs = Own(_device.CreatePixelShader(OceanShader.Compile(path, "WaterPS", "ps_5_0").Span));
            _compositePs = Own(_device.CreatePixelShader(OceanShader.Compile(path, "CompositePS", "ps_5_0").Span));
            _constants = Own(_device.CreateBuffer(57 * 16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            _indices = Own(_device.CreateBuffer(Columns * Rows * 6 * 4, BindFlags.IndexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var indices = new int[Columns * Rows * 6];
            var index = 0;
            for (var y = 0; y < Rows; y++)
            for (var x = 0; x < Columns; x++)
            {
                var a = y * (Columns + 1) + x;
                var b = a + Columns + 1;
                indices[index++] = a; indices[index++] = b; indices[index++] = a + 1;
                indices[index++] = a + 1; indices[index++] = b; indices[index++] = b + 1;
            }
            var mapped = _context.Map(_indices, 0, MapMode.WriteDiscard);
            try { Marshal.Copy(indices, 0, mapped.DataPointer, indices.Length); }
            finally { _context.Unmap(_indices, 0); }
            _depthOn = Own(_device.CreateDepthStencilState(DepthStencilDescription.Default));
            _depthOff = Own(_device.CreateDepthStencilState(DepthStencilDescription.None));
            _rasterizer = Own(_device.CreateRasterizerState(RasterizerDescription.CullNone));
            _sampler = Own(_device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
                MaxLOD = float.MaxValue, MaxAnisotropy = 1
            }));
            _surfaceSampler = Own(_device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.Anisotropic, AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap,
                MaxLOD = float.MaxValue, MaxAnisotropy = 8
            }));
            AppLog.Write($"Ocean sky refinement initialized. Size={width}x{height}; Adapter={AdapterName}; " +
                $"Preview={preview}; Presentation={(preview ? "OffscreenGdi" : "FlipDiscard")}; WindowSwapChain={_swap is not null}");
        }
        catch { Dispose(); throw; }
    }

    private void PrepareScene(OceanSettings settings)
    {
        var size = settings.InternalSize(Width, Height);
        if (size == (InternalWidth, InternalHeight)) return;
        _context.PSSetShaderResource(0, null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
        ReleaseScene();
        try
        {
            _scene = SceneOwn(_device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,
                (uint)size.Width, (uint)size.Height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource)));
            _sceneTarget = SceneOwn(_device.CreateRenderTargetView(_scene));
            _sceneRead = SceneOwn(_device.CreateShaderResourceView(_scene));
            var depth = SceneOwn(_device.CreateTexture2D(new Texture2DDescription(Format.D32_Float,
                (uint)size.Width, (uint)size.Height, 1, 1, BindFlags.DepthStencil)));
            _depth = SceneOwn(_device.CreateDepthStencilView(depth));
            InternalWidth = size.Width; InternalHeight = size.Height;
        }
        catch { ReleaseScene(); throw; }
    }

    private unsafe void SetConstants(double time, OceanSettings settings)
    {
        var mapped = _context.Map(_constants, 0, MapMode.WriteDiscard);
        try
        {
            var data = new Span<Vector4>((void*)mapped.DataPointer, 57);
            data.Slice(45).Clear();
            data[0] = new(InternalWidth, InternalHeight, (float)Width / Height, .383864f);
            data[1] = new(0, settings.Horizon ? 2.8f : 1.7f, -4, settings.Horizon ? .16f : .40f);
            var palette = OceanLightingModel.For(settings.Lighting, settings.Atmosphere);
            data[2] = new(palette.Direction, OceanLightingModel.AngularRadius);
            data[3] = palette.Radiance; data[4] = palette.Top;
            data[5] = palette.Horizon; data[6] = new(palette.Water, OceanWaveModel.Choppiness);
            if (settings.Surface == OceanSurface.Spectral) data[6].W = OceanSpectrum.Choppiness;
            data[7] = new(Columns, Rows, (int)settings.Lighting, settings.Surface == OceanSurface.Analytic ? 1 : 0);
            OceanWaveModel.Write(data.Slice(8, 32), time, settings.Agitation);
            data[40] = new(192, .50f + .60f * settings.Agitation, 0, 0);
            data[41] = new(28, .27f + .90f * settings.Agitation, 0, 0);
            data[42] = new(4.5f, .22f + .88f * settings.Agitation, 0, 0);
            if(FlatDiagnosticSurface)
            {
                for(var i=8;i<40;i++) data[i].Z=0;
                for(var i=40;i<43;i++) data[i].Y=0;
            }
            data[43] = new(settings.Atmosphere && settings.RefinedSky ? 1 : 0, _clouds?.Blend ?? 0,
                settings.GibbousMoon ? 1 : 0, !settings.Bloom || settings.Quality == OceanQuality.Economy ? 0 : .14f);
            data[44] = new(OceanLightingModel.ApparentRadius(MathF.Asin(palette.Direction.Y),
                settings.Atmosphere && settings.RefinedSky && settings.HorizonMagnification), 1.18f, 0, 0);
            if(CelestialFrame is { } frame)
            {
                float VisualRadius(OceanCelestialBody body) => body.Radius * OceanLightingModel.ApparentRadius((float)(body.AirlessElevation*OceanCelestialModel.Deg),settings.HorizonMagnification)/OceanLightingModel.AngularRadius;
                data[2]=new(frame.Sun.Direction,frame.Sun.Radius);
                data[3]=new(frame.SunRadiance,frame.Exposure); data[4]=new(0,0,0,1);
                var volume=Vector3.Lerp(new(.0006f,.0016f,.0023f),new(.0025f,.009f,.014f),frame.Daylight);
                data[6]=new(volume/Math.Max(1,frame.Exposure),data[6].W);
                data[45]=new(1,_cycleSky?.Blend ?? 0,OceanOpticalDepth.Aerosol(frame.Settings.Air),frame.Daylight);
                data[46]=new(VisualRadius(frame.Sun),frame.Sun.ApparentElevation,frame.Sun.VerticalScale,0);
                data[47]=new(frame.Moon.Direction,frame.Moon.Radius);
                data[48]=new(frame.MoonRadiance,0);
                data[49]=new(VisualRadius(frame.Moon),frame.Moon.ApparentElevation,frame.Moon.VerticalScale,frame.PhaseAngle);
                data[50]=new(frame.MoonLight,0); data[51]=new(frame.MoonPrime,0);
                data[52]=new(frame.MoonEast,0); data[53]=new(frame.MoonNorth,0);
                data[54]=new((int)Diagnostic,FlatDiagnosticSurface ? 1 : 0,0,0);
                if(settings.Weather is { } weather && _volumes is { } volumes)
                {
                    data[55]=new(1,volumes.Blend,0,0);
                    data[56]=new(32,16,weather.Layer.Top,volumes.Budget.LightSize);
                }
            }
        }
        finally { _context.Unmap(_constants, 0); }
    }

    public void Render(double time, OceanSettings settings, bool present = false, DateTimeOffset? celestialUtc = null, double? weatherTime = null,
        OceanCloudTrajectory? cloudTrajectory = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings = settings.Normalize();
        CelestialFrame = settings.Celestial is { } cycle && settings.Atmosphere && settings.RefinedSky
            ? OceanCelestialModel.Evaluate(celestialUtc ?? cycle.EpochUtc.AddSeconds(time*cycle.TimeScale/settings.Speed),cycle) : null;
        PrepareScene(settings);
        _context.PSSetShaderResource(10, null!);
        for (uint slot = 11; slot <= 27; slot++) _context.PSSetShaderResource(slot, null!);
        _reflectionLut ??= Own(new OceanReflectionLut(_device,_context));
        _context.PSSetShaderResource(16,_reflectionLut.Read);
        if (settings.Atmosphere)
        {
            _atmosphere ??= Own(new OceanAtmosphere(_device, _context));
            if(CelestialFrame is { } celestial)
            {
                _opticalDepth ??= Own(new OceanOpticalDepth(_device,_context));
                _context.CSSetShaderResource(17,_opticalDepth.Read); _context.CSSetSampler(3,_sampler);
                _context.PSSetShaderResource(17,_opticalDepth.Read); _context.PSSetSampler(3,_sampler);
                if(settings.Weather is not null)
                {
                    if(_volumes is null || _volumes.Quality!=settings.Quality)
                    { _volumes?.Dispose(); _volumes=null; _volumes=new OceanVolumetrics(_device,_context,settings.Quality); }
                    _volumes.Update(weatherTime ?? time/settings.Speed,settings,celestial,cloudTrajectory);
                    _volumes.Bind();
                }
                else
                {
                    if(_cycleSky is null || _cycleSky.Quality!=settings.Quality)
                    { _cycleSky?.Dispose(); _cycleSky=new OceanCycleSky(_device,_context,settings.Quality); }
                    _cycleSky.Update(celestial);
                    _context.PSSetShaderResource(10,_cycleSky.Previous); _context.PSSetShaderResource(15,_cycleSky.Next);
                }
            }
            else
            {
                _atmosphere.Update(settings.Lighting, settings.RefinedSky, settings.RefinedSky && settings.Lighting == OceanLighting.Moon && settings.GibbousMoon);
                _context.PSSetShaderResource(10, _atmosphere.Read);
            }
            _context.PSSetSampler(2, _atmosphere.Sampler);
            if (settings.RefinedSky)
            {
                _moon ??= Own(new OceanMoonTexture(_device, _context));
                if (settings.Weather is null || CelestialFrame is null)
                {
                    if (_clouds is null || _clouds.Quality != settings.Quality)
                    {
                        _clouds?.Dispose(); _clouds = null;
                        _clouds = new OceanClouds(_device, _context, settings.Quality);
                    }
                    _clouds.Update(time, settings, CelestialFrame);
                    _context.PSSetShaderResource(12, _clouds.Previous);
                    _context.PSSetShaderResource(13, _clouds.Next);
                }
                _context.PSSetShaderResource(11, _moon.Read);
            }
        }
        // Unbind all readers before updating their compute textures/mip chains.
        for (uint slot = 1; slot <= 9; slot++)
        {
            _context.VSSetShaderResource(slot, null!);
            _context.PSSetShaderResource(slot, null!);
        }
        if (settings.Surface == OceanSurface.Spectral)
        {
            _spectrum ??= Own(new OceanSpectrum(_device, _context));
            _spectrum.Update(time);
            for (uint band = 0; band < 3; band++)
            {
                var field = _spectrum.Bands[band];
                _context.VSSetShaderResource(1 + band, field.Displacement.Read);
                _context.PSSetShaderResource(1 + band, field.Displacement.Read);
                _context.PSSetShaderResource(4 + band, field.DerivativesA.Read);
                _context.PSSetShaderResource(7 + band, field.DerivativesB.Read);
            }
        }
        SetConstants(time, settings);
        _context.VSSetSampler(1, _surfaceSampler);
        _context.PSSetSampler(1, _surfaceSampler);
        _context.PSSetSampler(0, _sampler);
        _context.RSSetState(_rasterizer);
        _context.RSSetViewport(new Viewport(0, 0, InternalWidth, InternalHeight));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetConstantBuffer(0, _constants);
        _context.PSSetConstantBuffer(0, _constants);
        _context.PSSetShaderResource(0, null!);
        _context.OMSetBlendState(null);
        _context.OMSetRenderTargets(_sceneTarget!);
        _context.OMSetDepthStencilState(_depthOff);
        _context.VSSetShader(_screenVs); _context.PSSetShader(_skyPs);
        _context.Draw(3, 0);
        _context.ClearDepthStencilView(_depth!, DepthStencilClearFlags.Depth, 1, 0);
        _context.OMSetRenderTargets(_sceneTarget!, _depth);
        _context.OMSetDepthStencilState(_depthOn);
        _context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
        _context.VSSetShader(_waterVs); _context.PSSetShader(_waterPs);
        _context.DrawIndexed(Columns * Rows * 6, 0, 0);
        _context.OMSetDepthStencilState(_depthOff);
        _context.VSSetShader(_screenVs);
        _context.PSSetSampler(0, _sampler);
        if (settings.Atmosphere && settings.RefinedSky && settings.Bloom && settings.Quality != OceanQuality.Economy)
        {
            _bloom ??= new OceanBloom(_device, _context, InternalWidth, InternalHeight);
            _bloom.Render(_sceneRead!, CelestialFrame?.Exposure ?? OceanLightingModel.For(settings.Lighting, true).Radiance.W);
            _context.PSSetShaderResource(14, _bloom.Read);
        }
        _context.OMSetRenderTargets(_target);
        _context.RSSetViewport(new Viewport(0, 0, Width, Height));
        _context.VSSetShader(_screenVs); _context.PSSetShader(_compositePs);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetShaderResource(0, _sceneRead!);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
        if (present) Present();
    }

    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_preview is not null) _preview.Present(_window);
        else _swap!.Present(0, PresentFlags.None).CheckError();
    }

    // Diagnostic readback only; normal preview reuses GpuPreviewSurface's staging texture.
    public byte[] Pixels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var description = _back.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device.CreateTexture2D(description);
        _context.CopyResource(staging, _back);
        var mapped = _context.Map(staging, 0, MapMode.Read);
        var pixels = new byte[Width * Height * 4];
        try
        {
            for (var y = 0; y < Height; y++)
                Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, pixels, y * Width * 4, Width * 4);
        }
        finally { _context.Unmap(staging, 0); }
        return pixels;
    }

    private void ReleaseScene()
    {
        _context?.PSSetShaderResource(14, null!);
        _bloom?.Dispose(); _bloom = null;
        for (var i = _sceneResources.Count - 1; i >= 0; i--) _sceneResources[i].Dispose();
        _sceneResources.Clear(); InternalWidth = InternalHeight = 0;
        _scene = null; _sceneTarget = null; _sceneRead = null; _depth = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context?.ClearState();
        _clouds?.Dispose(); _clouds = null;
        _cycleSky?.Dispose(); _cycleSky = null;
        _volumes?.Dispose(); _volumes = null;
        ReleaseScene();
        for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _owned.Clear();
    }
}
