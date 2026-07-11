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

internal sealed class AethelisGpuRenderer : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly IDXGIFactory2 _factory;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11Texture2D _backBuffer;
    private readonly ID3D11RenderTargetView _renderTarget;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11ComputeShader? _computeShader;
    private readonly ID3D11Buffer _frameBuffer;
    private readonly Dictionary<string, EffekseerBridge> _effekseerByViewport = [];
    private readonly Dictionary<string, FluidResources> _fluidByViewport = [];
    private readonly bool _usesFireRingEffect;
    private readonly bool _usesFluidSimulation;
    private readonly string _effectPath;
    private readonly int _width;
    private readonly int _height;

    public AethelisGpuRenderer(IntPtr hwnd, int width, int height, string shaderFileName = "Aethelis.hlsl")
    {
        _width = width;
        _height = height;
        _factory = CreateDXGIFactory1<IDXGIFactory2>();

        using var adapter = GetHardwareAdapter(_factory);
        var flags = DeviceCreationFlags.BgraSupport;
        D3D11CreateDevice(adapter, DriverType.Unknown, flags, FeatureLevels,
            out _device, out var featureLevel, out _context).CheckError();

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
        _swapChain = _factory.CreateSwapChainForHwnd(_device, hwnd, description, fullscreen);
        _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(_backBuffer);

        var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", shaderFileName);
        var vertexBytecode = Compiler.CompileFromFile(shaderPath, "VSMain", "vs_5_0");
        var pixelBytecode = Compiler.CompileFromFile(shaderPath, "PSMain", "ps_5_0");
        _vertexShader = _device.CreateVertexShader(vertexBytecode.Span);
        _pixelShader = _device.CreatePixelShader(pixelBytecode.Span);
        _usesFluidSimulation = string.Equals(shaderFileName, "HypnixVolumetricFire.hlsl", StringComparison.OrdinalIgnoreCase);
        if (_usesFluidSimulation)
        {
            var computeBytecode = Compiler.CompileFromFile(shaderPath, "CSMain", "cs_5_0");
            _computeShader = _device.CreateComputeShader(computeBytecode.Span);
        }
        _frameBuffer = _device.CreateBuffer((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic, CpuAccessFlags.Write);
        _usesFireRingEffect = string.Equals(shaderFileName, "AethelisFlameBurst.hlsl", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(shaderFileName, "FlamethrowerRingV2.hlsl", StringComparison.OrdinalIgnoreCase);
        _effectPath = string.Equals(shaderFileName, "FlamethrowerRingV2.hlsl", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "FlamethrowerRingV2", "Runtime", "HypnixFlamethrower.efkefc")
            : Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "HypnixFireRing", "HypnixFireRing.efkefc");

        AppLog.Write($"Aethelis GPU renderer initialized. Shader={shaderFileName}; Size={width}x{height}; " +
                     $"FeatureLevel={featureLevel}; Adapter={adapter.Description1.Description}");
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
        AethelisAudioProfile profile, VisualizerSettings settings, bool freezeEffect = false)
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
            OriginY = y
        };

        var mapped = _context.Map(_frameBuffer, 0, MapMode.WriteDiscard);
        *(FrameConstants*)mapped.DataPointer = frame;
        _context.Unmap(_frameBuffer, 0);

        _context.OMSetRenderTargets(_renderTarget);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _frameBuffer);
        _context.RSSetViewport(new Viewport(x, y, width, height));
        if (_usesFluidSimulation && _computeShader is not null)
        {
            var viewportKey = $"{x}:{y}:{width}:{height}";
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
            _context.PSSetShaderResource(0, null);
        }
        else
        {
            _context.Draw(3, 0);
        }
        if (_usesFireRingEffect)
        {
            var viewportKey = $"{x}:{y}:{width}:{height}";
            if (!_effekseerByViewport.TryGetValue(viewportKey, out var effekseer))
            {
                effekseer = EffekseerBridge.TryCreate(_device.NativePointer, _context.NativePointer, _effectPath);
                if (effekseer is not null) _effekseerByViewport.Add(viewportKey, effekseer);
            }

            if (!freezeEffect) effekseer?.Update(time, profile, settings.Intensity);
            effekseer?.Render(width / (float)Math.Max(1, height));
        }
    }

    public void EndFrame()
    {
        _swapChain.Present(0, PresentFlags.None);
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
        _context.ClearState();
        _context.Flush();
        foreach (var effekseer in _effekseerByViewport.Values) effekseer.Dispose();
        _effekseerByViewport.Clear();
        foreach (var fluid in _fluidByViewport.Values) fluid.Dispose();
        _fluidByViewport.Clear();
        _computeShader?.Dispose();
        _frameBuffer.Dispose();
        _pixelShader.Dispose();
        _vertexShader.Dispose();
        _renderTarget.Dispose();
        _backBuffer.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
        _factory.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
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
        public float PaddingX;
        public float PaddingY;
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
            for (var i = 0; i < 2; i++)
            {
                _textures[i] = device.CreateTexture3D(description);
                _views[i] = device.CreateShaderResourceView(_textures[i]);
                _targets[i] = device.CreateUnorderedAccessView(_textures[i]);
                context.ClearUnorderedAccessView(_targets[i], System.Numerics.Vector4.Zero);
            }
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
            foreach (var target in _targets) target.Dispose();
            foreach (var view in _views) view.Dispose();
            foreach (var texture in _textures) texture.Dispose();
        }
    }
}
