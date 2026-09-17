using System.Runtime.InteropServices;
using System.IO;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed partial class AethelisGpuRenderer : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0
    ];

    private readonly IDXGIFactory2 _factory;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11Texture2D _backBuffer;
    private readonly ID3D11RenderTargetView _renderTarget;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11PixelShader? _bloomHorizontalShader;
    private readonly ID3D11PixelShader? _bloomVerticalShader;
    private readonly ID3D11PixelShader? _compositeShader;
    private readonly ID3D11ComputeShader? _computeShader;
    private readonly ID3D11Buffer _frameBuffer;
    // Keyed by the viewport rectangle as a value tuple: lookups allocate nothing, unlike the
    // former interpolated "{x}:{y}:{w}:{h}" string that was rebuilt every frame per viewport.
    private readonly Dictionary<(int X, int Y, int Width, int Height), EffekseerBridge> _effekseerByViewport = [];
    private readonly Dictionary<(int X, int Y, int Width, int Height), FluidResources> _fluidByViewport = [];
    private readonly Dictionary<(int X, int Y, int Width, int Height), FeedbackResources> _feedbackByViewport = [];
    private readonly HashSet<(int X, int Y, int Width, int Height)> _failedEffectViewports = [];
    private readonly List<IDisposable> _deviceResources = [];
    private bool _disposed;
    private readonly bool _usesFireRingEffect;
    private readonly bool _usesFluidSimulation;
    private readonly bool _usesFeedback;
    private readonly bool _usesEventHorizon;
    private readonly bool _usesFractalPyramid;
    private readonly bool _usesKaleidoscope;
    private readonly bool _usesLotus;
    private readonly bool _usesLiquidOrbs;
    private readonly ID3D11SamplerState? _linearSampler;
    // Optional custom background (solid color or cover image) composited behind the effect and
    // then screen-blended over. Inactive by default ("original"), leaving rendering unchanged.
    private readonly ID3D11VertexShader _backgroundVertexShader;
    private readonly ID3D11PixelShader _backgroundPixelShader;
    private readonly ID3D11Buffer _backgroundBuffer;
    private readonly ID3D11BlendState _screenBlend;
    private readonly FireBackground _background;
    private readonly string _effectPath;
    private readonly int _width;
    private readonly int _height;

    public AethelisGpuRenderer(IntPtr hwnd, int width, int height, string shaderFileName = "Aethelis.hlsl")
    {
        _width = width;
        _height = height;
        try
        {
            _factory = Own(CreateDXGIFactory1<IDXGIFactory2>());

            using var adapter = GetHardwareAdapter(_factory);
            var flags = DeviceCreationFlags.BgraSupport;
            D3D11CreateDevice(adapter, DriverType.Unknown, flags, FeatureLevels,
                out _device, out var featureLevel, out _context).CheckError();
            Own(_device);
            Own(_context);

            var description = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore
            };
            var fullscreen = new SwapChainFullscreenDescription { Windowed = true };
            _swapChain = Own(_factory.CreateSwapChainForHwnd(_device, hwnd, description, fullscreen));
            _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
            _backBuffer = Own(_swapChain.GetBuffer<ID3D11Texture2D>(0));
            _renderTarget = Own(_device.CreateRenderTargetView(_backBuffer));

            var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", shaderFileName);
            var vertexBytecode = Compiler.CompileFromFile(shaderPath, "VSMain", "vs_5_0");
            var pixelBytecode = Compiler.CompileFromFile(shaderPath, "PSMain", "ps_5_0");
            _vertexShader = Own(_device.CreateVertexShader(vertexBytecode.Span));
            _pixelShader = Own(_device.CreatePixelShader(pixelBytecode.Span));
            _usesFluidSimulation = string.Equals(shaderFileName, "HypnixVolumetricFire.hlsl", StringComparison.OrdinalIgnoreCase);
            if (_usesFluidSimulation)
            {
                var computeBytecode = Compiler.CompileFromFile(shaderPath, "CSMain", "cs_5_0");
                _computeShader = Own(_device.CreateComputeShader(computeBytecode.Span));
            }
            _usesFeedback = string.Equals(shaderFileName, "SpectralBloom.hlsl", StringComparison.OrdinalIgnoreCase);
            _usesEventHorizon = string.Equals(shaderFileName, "EventHorizon.hlsl", StringComparison.OrdinalIgnoreCase);
            _usesFractalPyramid = string.Equals(shaderFileName, "FractalPyramid.hlsl", StringComparison.OrdinalIgnoreCase);
            _usesKaleidoscope = string.Equals(shaderFileName, "Kaleidoscope.hlsl", StringComparison.OrdinalIgnoreCase);
            _usesLotus = string.Equals(shaderFileName, "Lotus.hlsl", StringComparison.OrdinalIgnoreCase);
            _usesLiquidOrbs = string.Equals(shaderFileName, "LiquidOrbs.hlsl", StringComparison.OrdinalIgnoreCase);
            if (_usesFeedback || _usesEventHorizon)
            {
                _bloomHorizontalShader = Own(_device.CreatePixelShader(Compiler.CompileFromFile(shaderPath, "PSBloomHorizontal", "ps_5_0").Span));
                _bloomVerticalShader = Own(_device.CreatePixelShader(Compiler.CompileFromFile(shaderPath, "PSBloomVertical", "ps_5_0").Span));
                _compositeShader = Own(_device.CreatePixelShader(Compiler.CompileFromFile(shaderPath, "PSComposite", "ps_5_0").Span));
            }
            // Always available: the feedback bloom and the background compositor both sample textures
            // with linear clamp (cover / edge) behavior.
            _linearSampler = Own(_device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
                MipLODBias = 0, MaxAnisotropy = 1, ComparisonFunc = ComparisonFunction.Never,
                MinLOD = 0, MaxLOD = float.MaxValue
            }));
            _frameBuffer = Own(_device.CreateBuffer((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic, CpuAccessFlags.Write));
            // Background compositor: a color/cover-image pass drawn behind the effect, with the effect
            // screen-blended on top so its dark areas reveal the background.
            var backgroundPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "GpuBackground.hlsl");
            _backgroundVertexShader = Own(_device.CreateVertexShader(Compiler.CompileFromFile(backgroundPath, "VSMain", "vs_5_0").Span));
            _backgroundPixelShader = Own(_device.CreatePixelShader(Compiler.CompileFromFile(backgroundPath, "PSMain", "ps_5_0").Span));
            _backgroundBuffer = Own(_device.CreateBuffer((uint)Marshal.SizeOf<BackgroundConstants>(), BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic, CpuAccessFlags.Write));
            // Screen blend for color: result = src*(1-dst) + dst, so a black effect keeps the
            // background and a bright effect adds over it without harshly saturating. Alpha uses
            // valid alpha-only factors (color factors like InverseDestinationColor are illegal for
            // the alpha channel and would fail device creation).
            _screenBlend = Own(_device.CreateBlendState(new BlendDescription(
                Blend.InverseDestinationColor, Blend.One, Blend.One, Blend.Zero)));
            _background = Own(new FireBackground(_device));
            _usesFireRingEffect = string.Equals(shaderFileName, "AethelisFlameBurst.hlsl", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(shaderFileName, "FlamethrowerRingV2.hlsl", StringComparison.OrdinalIgnoreCase);
            _effectPath = string.Equals(shaderFileName, "FlamethrowerRingV2.hlsl", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "FlamethrowerRingV2", "Runtime", "HypnixFlamethrower.efkefc")
                : Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "HypnixFireRing", "HypnixFireRing.efkefc");

            AppLog.Write($"Aethelis GPU renderer initialized. Shader={shaderFileName}; Size={width}x{height}; " +
                         $"FeatureLevel={featureLevel}; Adapter={adapter.Description1.Description}");
        }
        catch { ReleaseDeviceResources(); throw; }
    }

    private T Own<T>(T resource) where T : IDisposable { _deviceResources.Add(resource); return resource; }
    private void ReleaseDeviceResources()
    {
        for (var i = _deviceResources.Count - 1; i >= 0; i--) _deviceResources[i].Dispose();
        _deviceResources.Clear();
    }

    public void BeginFrame()
    {
        _context.ClearRenderTargetView(_renderTarget, new Color4(0.003f, 0.006f, 0.018f, 1));
        _context.OMSetRenderTargets(_renderTarget);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _frameBuffer);
    }

    public unsafe void RenderViewport(int x, int y, int width, int height, double time,
        AethelisAudioProfile profile, VisualizerSettings settings, bool freezeEffect = false, float[]? spectrum = null)
    {
        var frame = new FrameConstants
        {
            ResolutionX = width,
            ResolutionY = height,
            Time = (float)time,
            Bass = profile.Bass,
            Mids = profile.Mids,
            Highs = profile.Highs,
            Intensity = settings.Intensity,
            Glow = settings.Glow,
            OriginX = x,
            OriginY = y,
            Scale = settings.Scale <= 0 ? 1f : settings.Scale, // user size/zoom
            OffsetX = settings.OffsetX,                         // user horizontal position
            OffsetY = settings.OffsetY,                         // user vertical position
            StartColorR = settings.StartColor.R / 255f,
            StartColorG = settings.StartColor.G / 255f,
            StartColorB = settings.StartColor.B / 255f,
            EndColorR = settings.EndColor.R / 255f,
            EndColorG = settings.EndColor.G / 255f,
            EndColorB = settings.EndColor.B / 255f
        };

        if (_usesLiquidOrbs)
        {
            // HYPNIX-original orb motion: per-orb hashed speed, phase and radius.
            // Centers depend only on time, so they are computed once per viewport
            // here (not per ray-march step). xyz = world center; w = base radius.
            for (var i = 0; i < 16; i++)
            {
                var fi = i + 1f;
                var h1 = MathF.Sin(fi * 12.9898f) * 43758.5453f; h1 -= MathF.Floor(h1);
                var h2 = MathF.Sin(fi * 78.233f) * 43758.5453f; h2 -= MathF.Floor(h2);
                var h3 = MathF.Sin(fi * 37.719f) * 43758.5453f; h3 -= MathF.Floor(h3);
                var phase = (float)time * (0.35f + 0.9f * h1);
                frame.Spheres[i * 4] = MathF.Sin(phase + h1 * 6.2831853f + i) * 2.1f;
                frame.Spheres[i * 4 + 1] = MathF.Cos(phase * 0.9f + h2 * 6.2831853f) * 1.9f;
                frame.Spheres[i * 4 + 2] = MathF.Sin(phase * 0.7f + h3 * 6.2831853f) * 0.8f;
                frame.Spheres[i * 4 + 3] = 0.55f + 0.35f * h2;
            }
        }

        if (_usesEventHorizon || _usesFractalPyramid || _usesKaleidoscope || _usesLotus)
        {
            // Feed the 64 logarithmic bands so the shader can drive brightness per frequency.
            // Capture already smooths attack/release; no extra audio history here.
            for (var i = 0; i < 64; i++)
                frame.Spectrum[i] = spectrum is { Length: > 0 }
                    ? AethelisAudioProfile.ApplyGain(spectrum[Math.Min(i * spectrum.Length / 64, spectrum.Length - 1)], settings.Sensitivity)
                    : i < 16 ? profile.Bass : i < 48 ? profile.Mids : profile.Highs;
        }

        if (_usesFeedback)
        {
            var key = (x, y, width, height);
            if (!_feedbackByViewport.TryGetValue(key, out var feedback))
            {
                feedback = new FeedbackResources(_device, _context, width, height);
                _feedbackByViewport.Add(key, feedback);
            }
            // Freeze keeps the history, bloom and audio envelope unchanged for this monitor.
            var dt = feedback.LastTime is { } last ? Math.Clamp(time - last, 0, 0.1) : 1.0 / 60;
            frame.PaddingX = (float)dt;
            if (!freezeEffect)
            {
                feedback.LastTime = time;
                for (var i = 0; i < 64; i++)
                {
                    var raw = spectrum is { Length: > 0 } ? spectrum[Math.Min(i * spectrum.Length / 64, spectrum.Length - 1)] : 0;
                    var target = AethelisAudioProfile.ApplyGain(raw, settings.Sensitivity);
                    var rate = target > feedback.Spectrum[i] ? 22f : 6f;
                    feedback.Spectrum[i] += (target - feedback.Spectrum[i]) * (1 - MathF.Exp(-rate * (float)dt));
                }
            }
            for (var i = 0; i < 64; i++) frame.Spectrum[i] = feedback.Spectrum[i];
        }

        var mapped = _context.Map(_frameBuffer, 0, MapMode.WriteDiscard);
        *(FrameConstants*)mapped.DataPointer = frame;
        _context.Unmap(_frameBuffer, 0);

        // Refresh the optional custom background; "original" (W=0) leaves the effect unchanged.
        _background.Update(settings.Background);
        var hasBackground = _background.Color.W > 0.5f;

        _context.OMSetRenderTargets(_renderTarget);
        _context.RSSetViewport(new Viewport(x, y, width, height));
        // Direct (non-feedback) effects draw the background here; feedback effects compose it just
        // before their final composite pass instead.
        if (hasBackground && !(_usesFeedback || _usesEventHorizon || _usesFluidSimulation))
            ComposeBackground(x, y, width, height);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _frameBuffer);
        if (_usesFeedback || _usesEventHorizon)
        {
            RenderFeedbackViewport(x, y, width, height, freezeEffect, hasBackground);
        }
        else if (_usesFluidSimulation && _computeShader is not null)
        {
            var viewportKey = (x, y, width, height);
            if (!_fluidByViewport.TryGetValue(viewportKey, out var fluid))
            {
                fluid = new FluidResources(_device, _context, width, height);
                _fluidByViewport.Add(viewportKey, fluid);
            }
            if (!freezeEffect) fluid.Advance(_context, _computeShader, _frameBuffer);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetConstantBuffer(0, _frameBuffer);
            _context.PSSetShaderResource(0, fluid.CurrentView);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
        }
        else
        {
            // Screen-blend the effect over the background when one is set; otherwise draw opaque.
            if (hasBackground) _context.OMSetBlendState(_screenBlend);
            _context.Draw(3, 0);
            if (hasBackground) _context.OMSetBlendState(null);
        }
        if (_usesFireRingEffect)
        {
            var viewportKey = (x, y, width, height);
            if (!_effekseerByViewport.TryGetValue(viewportKey, out var effekseer) && !_failedEffectViewports.Contains(viewportKey))
            {
                effekseer = EffekseerBridge.TryCreate(_device.NativePointer, _context.NativePointer, _effectPath);
                if (effekseer is not null) _effekseerByViewport.Add(viewportKey, effekseer);
                else _failedEffectViewports.Add(viewportKey);
            }

            if (!freezeEffect) effekseer?.Update(time, profile, settings.Intensity);
            effekseer?.Render(width / (float)Math.Max(1, height));
        }
    }

    // Per-viewport HDR rendering and bloom. Spectral Bloom uses temporal feedback;
    // Event Horizon writes an independent frame without sampling previous emission.
    // When frozen the buffer is not advanced; the last frame is simply re-composited, so the monitor holds still.
    private void RenderFeedbackViewport(int x, int y, int width, int height, bool freezeEffect, bool hasBackground = false)
    {
        var viewportKey = (x, y, width, height);
        if (!_feedbackByViewport.TryGetValue(viewportKey, out var feedback))
        {
            feedback = new FeedbackResources(_device, _context, width, height);
            _feedbackByViewport.Add(viewportKey, feedback);
        }

        // Initialize a newly created frozen viewport once, then reuse its HDR image.
        if (!freezeEffect || (_usesEventHorizon && !feedback.HasFrame))
        {
            _context.OMSetRenderTargets(feedback.NextTarget);
            _context.RSSetViewport(new Viewport(0, 0, width, height));
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.PSSetConstantBuffer(0, _frameBuffer);
            _context.PSSetShaderResource(0, feedback.CurrentView);
            if (_linearSampler is not null) _context.PSSetSampler(0, _linearSampler);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
            feedback.Swap();

            _context.RSSetViewport(new Viewport(0, 0, feedback.BloomWidth, feedback.BloomHeight));
            _context.OMSetRenderTargets(feedback.BloomTargets[0]);
            _context.PSSetShader(_bloomHorizontalShader);
            _context.PSSetShaderResource(0, feedback.CurrentView);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
            _context.OMSetRenderTargets(feedback.BloomTargets[1]);
            _context.PSSetShader(_bloomVerticalShader);
            _context.PSSetShaderResource(0, feedback.BloomViews[0]);
            _context.Draw(3, 0);
            _context.PSSetShaderResource(0, null!);
        }

        _context.OMSetRenderTargets(_renderTarget);
        _context.RSSetViewport(new Viewport(x, y, width, height));
        // Draw the background first, then screen-blend the composited effect over it.
        if (hasBackground) ComposeBackground(x, y, width, height);
        _context.OMSetRenderTargets(_renderTarget);
        _context.RSSetViewport(new Viewport(x, y, width, height));
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_compositeShader);
        _context.PSSetConstantBuffer(0, _frameBuffer);
        _context.PSSetSampler(0, _linearSampler);
        _context.PSSetShaderResource(0, feedback.CurrentView);
        _context.PSSetShaderResource(1, feedback.BloomViews[1]);
        if (hasBackground) _context.OMSetBlendState(_screenBlend);
        _context.Draw(3, 0);
        if (hasBackground) _context.OMSetBlendState(null);
        _context.PSSetShaderResource(0, null!);
        _context.PSSetShaderResource(1, null!);
    }

    // Draws the custom background (solid color or cover image) into the viewport as an opaque pass.
    // The caller then screen-blends the effect over it. Uses the shared linear-clamp sampler.
    private unsafe void ComposeBackground(int x, int y, int width, int height)
    {
        var data = new BackgroundConstants
        {
            ColorR = _background.Color.X, ColorG = _background.Color.Y, ColorB = _background.Color.Z, Mode = _background.Color.W,
            ImageW = _background.Size.X, ImageH = _background.Size.Y, ViewW = width, ViewH = height
        };
        var mapped = _context.Map(_backgroundBuffer, 0, MapMode.WriteDiscard);
        *(BackgroundConstants*)mapped.DataPointer = data;
        _context.Unmap(_backgroundBuffer, 0);
        _context.OMSetBlendState(null);
        _context.OMSetRenderTargets(_renderTarget);
        _context.RSSetViewport(new Viewport(x, y, width, height));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_backgroundVertexShader);
        _context.PSSetShader(_backgroundPixelShader);
        _context.PSSetConstantBuffer(0, _backgroundBuffer);
        _context.PSSetSampler(0, _linearSampler);
        // Null in solid-color mode simply clears the slot; the shader only samples in image mode.
        _context.PSSetShaderResource(0, _background.Image!);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
    }

    public void EndFrame()
    {
        _swapChain.Present(0, PresentFlags.None).CheckError();
    }

    private static IDXGIAdapter1 GetHardwareAdapter(IDXGIFactory2 factory)
    {
        using var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();
        if (factory6 is not null)
        {
            for (uint index = 0; factory6.EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance,
                     out IDXGIAdapter1? adapter).Success; index++)
            {
                if (adapter is not null && (adapter.Description1.Flags & AdapterFlags.Software) == 0)
                    return adapter;
                adapter?.Dispose();
            }
        }

        for (uint index = 0; factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success; index++)
        {
            if (adapter is not null && (adapter.Description1.Flags & AdapterFlags.Software) == 0)
                return adapter;
            adapter?.Dispose();
        }

        throw new InvalidOperationException("No Direct3D 11 hardware adapter is available.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
        _context.ClearState();
        _context.Flush();
        foreach (var effekseer in _effekseerByViewport.Values) effekseer.Dispose();
        _effekseerByViewport.Clear();
        foreach (var fluid in _fluidByViewport.Values) fluid.Dispose();
        _fluidByViewport.Clear();
        foreach (var feedback in _feedbackByViewport.Values) feedback.Dispose();
        _feedbackByViewport.Clear();
        ReleaseDeviceResources();
    }

    // Safety net: runs only if Dispose was skipped. The Direct3D COM objects release through
    // their own finalizers, so here we just record the missed disposal for diagnosis. A
    // finalizer must never throw.
    ~AethelisGpuRenderer()
    {
        try
        {
            if (!_disposed)
                AppLog.Write("AethelisGpuRenderer finalized without Dispose(); GPU resources left to COM finalizers.");
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FrameConstants
    {
        public float ResolutionX;
        public float ResolutionY;
        public float Time;
        public float Bass;
        public float Mids;
        public float Highs;
        public float Intensity;
        public float Glow;
        public float OriginX;
        public float OriginY;
        public float PaddingX;   // reused as feedback delta-time for the bloom modes
        public float OffsetY;    // free padding slot -> user vertical position
        public float StartColorR;
        public float StartColorG;
        public float StartColorB;
        public float Scale;      // free padding slot -> user size/zoom
        public float EndColorR;
        public float EndColorG;
        public float EndColorB;
        public float OffsetX;    // free padding slot -> user horizontal position
        // float4[16] in HLSL: scalar arrays would have a different cbuffer stride.
        public fixed float Spectrum[64];
        public fixed float Spheres[64];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BackgroundConstants
    {
        public float ColorR, ColorG, ColorB, Mode;   // rgb + mode (1 solid, 2 image)
        public float ImageW, ImageH, ViewW, ViewH;    // image pixels + viewport pixels
    }

    private sealed class FluidResources : IDisposable
    {
        private readonly ID3D11Texture3D[] _textures = new ID3D11Texture3D[2];
        private readonly ID3D11ShaderResourceView[] _views = new ID3D11ShaderResourceView[2];
        private readonly ID3D11UnorderedAccessView[] _targets = new ID3D11UnorderedAccessView[2];
        private readonly uint _width;
        private readonly uint _height;
        private readonly uint _depth;
        private int _current;

        public FluidResources(ID3D11Device device, ID3D11DeviceContext context, int viewportWidth, int viewportHeight)
        {
            _width = 192;
            _height = (uint)Math.Clamp((int)Math.Round(_width * viewportHeight / (double)Math.Max(1, viewportWidth)), 80, 128);
            _depth = 64;
            var description = new Texture3DDescription(Format.R16G16B16A16_Float, _width, _height, _depth, 1,
                BindFlags.ShaderResource | BindFlags.UnorderedAccess);
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    _textures[i] = device.CreateTexture3D(description);
                    _views[i] = device.CreateShaderResourceView(_textures[i]);
                    _targets[i] = device.CreateUnorderedAccessView(_textures[i]);
                    context.ClearUnorderedAccessView(_targets[i], System.Numerics.Vector4.Zero);
                }
            }
            catch { Dispose(); throw; }
            AppLog.Write($"Volumetric GPU state initialized and cleared. Grid={_width}x{_height}x{_depth}; Viewport={viewportWidth}x{viewportHeight}");
        }

        public ID3D11ShaderResourceView CurrentView => _views[_current];

        public void Advance(ID3D11DeviceContext context, ID3D11ComputeShader shader, ID3D11Buffer constants)
        {
            var next = 1 - _current;
            context.CSSetShader(shader);
            context.CSSetConstantBuffer(0, constants);
            context.CSSetShaderResource(0, _views[_current]);
            context.CSSetUnorderedAccessView(0, _targets[next]);
            context.Dispatch((_width + 7) / 8, (_height + 3) / 4, (_depth + 3) / 4);
            context.CSSetUnorderedAccessView(0, null);
            context.CSSetShaderResource(0, null);
            context.CSSetShader(null);
            _current = next;
        }

        public void Dispose()
        {
            // Null-safe: a failed constructor can leave some slots unassigned.
            foreach (var target in _targets) target?.Dispose();
            foreach (var view in _views) view?.Dispose();
            foreach (var texture in _textures) texture?.Dispose();
        }
    }

    // Per-viewport ping-pong render targets that hold the previous frame for MilkDrop-style feedback.
    private sealed class FeedbackResources : IDisposable
    {
        private readonly ID3D11Texture2D[] _textures = new ID3D11Texture2D[2];
        private readonly ID3D11RenderTargetView[] _targets = new ID3D11RenderTargetView[2];
        private readonly ID3D11ShaderResourceView[] _views = new ID3D11ShaderResourceView[2];
        private int _current;
        private readonly List<IDisposable> _owned = [];
        public double? LastTime { get; set; }
        public bool HasFrame { get; private set; }
        public float[] Spectrum { get; } = new float[64];
        public int BloomWidth { get; }
        public int BloomHeight { get; }
        public ID3D11RenderTargetView[] BloomTargets { get; } = new ID3D11RenderTargetView[2];
        public ID3D11ShaderResourceView[] BloomViews { get; } = new ID3D11ShaderResourceView[2];

        public FeedbackResources(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            BloomWidth = Math.Max(1, width / 2);
            BloomHeight = Math.Max(1, height / 2);
            var description = new Texture2DDescription(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1,
                BindFlags.RenderTarget | BindFlags.ShaderResource);
            try
            {
            for (var i = 0; i < 2; i++)
            {
                _textures[i] = Own(device.CreateTexture2D(description));
                _targets[i] = Own(device.CreateRenderTargetView(_textures[i]));
                _views[i] = Own(device.CreateShaderResourceView(_textures[i]));
                context.ClearRenderTargetView(_targets[i], new Color4(0, 0, 0, 1));
            }
            description.Width = (uint)BloomWidth;
            description.Height = (uint)BloomHeight;
            for (var i = 0; i < 2; i++)
            {
                var texture = Own(device.CreateTexture2D(description));
                BloomTargets[i] = Own(device.CreateRenderTargetView(texture));
                BloomViews[i] = Own(device.CreateShaderResourceView(texture));
                context.ClearRenderTargetView(BloomTargets[i], new Color4(0, 0, 0, 1));
            }
            }
            catch { Dispose(); throw; }
        }

        private T Own<T>(T resource) where T : IDisposable { _owned.Add(resource); return resource; }

        public ID3D11RenderTargetView NextTarget => _targets[1 - _current];
        public ID3D11ShaderResourceView CurrentView => _views[_current];
        public ID3D11Texture2D CurrentTexture => _textures[_current];
        public void Swap() { _current = 1 - _current; HasFrame = true; }

        public void Dispose()
        {
            for (var i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
        }
    }
}
