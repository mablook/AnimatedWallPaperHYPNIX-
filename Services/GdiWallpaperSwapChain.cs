using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace AnimatedWallPaper.Services;

// Presents a GDI-rendered frame through a DXGI flip swap chain.
//
// The classic wallpapers (Built-in ambient, Audio Visualizer) draw with System.Drawing into a
// back buffer that used to be blitted to the window with GDI. On the Windows 11 "raised desktop"
// (Progman carries WS_EX_NOREDIRECTIONBITMAP) a GDI child of Progman has no redirection surface,
// so DWM never composites the blit and the wallpaper shows black -- while the shader wallpapers,
// which own a DXGI swap chain, display correctly. Routing the same GDI frame through a swap chain
// gives these wallpapers their own composition surface and makes them appear on the desktop. The
// GDI drawing is unchanged; only presentation moves to the GPU.
//
// Created, used and disposed on the render thread, like the other GPU renderers.
internal sealed class GdiWallpaperSwapChain : IDisposable
{
    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];

    private readonly IDXGIFactory2 _factory;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11SamplerState _sampler;
    private ID3D11Texture2D _backBuffer;
    private ID3D11RenderTargetView _renderTarget;
    private ID3D11Texture2D _frameTexture;
    private ID3D11ShaderResourceView _frameView;
    private int _width;
    private int _height;
    private bool _disposed;

    public GdiWallpaperSwapChain(IntPtr hwnd, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        try
        {
            _factory = CreateDXGIFactory1<IDXGIFactory2>();
            using var adapter = GetHardwareAdapter(_factory);
            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels,
                out _device, out var featureLevel, out _context).CheckError();

            var description = new SwapChainDescription1
            {
                Width = (uint)_width,
                Height = (uint)_height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = SampleDescription.Default,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore
            };
            _swapChain = _factory.CreateSwapChainForHwnd(_device, hwnd, description,
                new SwapChainFullscreenDescription { Windowed = true });
            _factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);
            _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
            _renderTarget = _device.CreateRenderTargetView(_backBuffer);

            var shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "GdiPresent.hlsl");
            _vertexShader = _device.CreateVertexShader(Compiler.CompileFromFile(shaderPath, "VSMain", "vs_5_0").Span);
            _pixelShader = _device.CreatePixelShader(Compiler.CompileFromFile(shaderPath, "PSMain", "ps_5_0").Span);
            _sampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
                MipLODBias = 0, MaxAnisotropy = 1, ComparisonFunc = ComparisonFunction.Never,
                MinLOD = 0, MaxLOD = float.MaxValue
            });
            CreateFrameTexture();
            AppLog.Write($"GDI wallpaper swap chain created. Size={_width}x{_height}; FeatureLevel={featureLevel}; " +
                         $"Adapter={adapter.Description1.Description}");
        }
        catch { Dispose(); throw; }
    }

    [MemberNotNull(nameof(_frameTexture), nameof(_frameView))]
    private void CreateFrameTexture()
    {
        // Dynamic texture updated from the GDI back buffer each frame, then sampled onto the swap chain.
        var description = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)_width, (uint)_height, 1, 1,
            BindFlags.ShaderResource);
        description.Usage = ResourceUsage.Dynamic;
        description.CPUAccessFlags = CpuAccessFlags.Write;
        _frameTexture = _device.CreateTexture2D(description);
        _frameView = _device.CreateShaderResourceView(_frameTexture);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_disposed || (width == _width && height == _height)) return;
        _width = width;
        _height = height;
        // Release every reference to the swap-chain buffers before resizing them.
        _context.ClearState();
        _renderTarget.Dispose();
        _backBuffer.Dispose();
        _swapChain.ResizeBuffers(2, (uint)_width, (uint)_height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        _backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(_backBuffer);
        _frameView.Dispose();
        _frameTexture.Dispose();
        CreateFrameTexture();
    }

    public void Present(Bitmap frame)
    {
        if (_disposed) return;
        DrawFrame(frame);
        _swapChain.Present(0, PresentFlags.None).CheckError();
    }

    // Uploads the CPU frame and draws it onto the swap-chain back buffer without presenting.
    // Shared by Present and the render-back verification used in tests.
    private unsafe void DrawFrame(Bitmap frame)
    {
        if (frame.Width != _width || frame.Height != _height) Resize(frame.Width, frame.Height);
        var bits = frame.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var mapped = _context.Map(_frameTexture, 0, MapMode.WriteDiscard);
            var rowBytes = (long)_width * 4;
            var source = (byte*)bits.Scan0;
            var destination = (byte*)mapped.DataPointer;
            for (var y = 0; y < _height; y++)
                Buffer.MemoryCopy(source + (long)y * bits.Stride, destination + (long)y * mapped.RowPitch, rowBytes, rowBytes);
            _context.Unmap(_frameTexture, 0);
        }
        finally { frame.UnlockBits(bits); }

        _context.OMSetRenderTargets(_renderTarget);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height));
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _context.PSSetSampler(0, _sampler);
        _context.PSSetShaderResource(0, _frameView);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
    }

    // Draws the frame and reads the resulting swap-chain back buffer back to the CPU (BGRA, opaque).
    // Test-only: it verifies the GPU presentation path without a live desktop or a real Present.
    internal byte[] CaptureForTest(Bitmap frame)
    {
        DrawFrame(frame);
        var description = _backBuffer.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device.CreateTexture2D(description);
        _context.CopyResource(staging, _backBuffer);
        var mapped = _context.Map(staging, 0, MapMode.Read);
        try
        {
            var pixels = new byte[_width * _height * 4];
            for (var y = 0; y < _height; y++)
                System.Runtime.InteropServices.Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch,
                    pixels, y * _width * 4, _width * 4);
            return pixels;
        }
        finally { _context.Unmap(staging, 0); }
    }

    private static IDXGIAdapter1 GetHardwareAdapter(IDXGIFactory2 factory)
    {
        using var factory6 = factory.QueryInterfaceOrNull<IDXGIFactory6>();
        if (factory6 is not null)
        {
            for (uint index = 0; factory6.EnumAdapterByGpuPreference(index, GpuPreference.HighPerformance,
                     out IDXGIAdapter1? adapter).Success; index++)
            {
                if (adapter is not null && (adapter.Description1.Flags & AdapterFlags.Software) == 0) return adapter;
                adapter?.Dispose();
            }
        }
        for (uint index = 0; factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success; index++)
        {
            if (adapter is not null && (adapter.Description1.Flags & AdapterFlags.Software) == 0) return adapter;
            adapter?.Dispose();
        }
        throw new InvalidOperationException("No Direct3D 11 hardware adapter is available.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context?.ClearState();
        _context?.Flush();
        _frameView?.Dispose();
        _frameTexture?.Dispose();
        _renderTarget?.Dispose();
        _backBuffer?.Dispose();
        _sampler?.Dispose();
        _pixelShader?.Dispose();
        _vertexShader?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
    }
}
