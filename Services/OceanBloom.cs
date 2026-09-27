using System.IO;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AnimatedWallPaper.Services;

// Bounded optical response in display-relative HDR. Runs after water and sky.
internal sealed class OceanBloom : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11PixelShader _extract, _blur;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11RenderTargetView[] _targets = new ID3D11RenderTargetView[2];
    private readonly ID3D11ShaderResourceView[] _reads = new ID3D11ShaderResourceView[2];
    public ID3D11ShaderResourceView Read => _reads[0];
    public int Width { get; }
    public int Height { get; }
    public long EstimatedBytes => (long)Width * Height * 16;
    private T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }
    public OceanBloom(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        _context = context; Width = Math.Max(1,(width+3)/4); Height = Math.Max(1,(height+3)/4);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory,"Shaders","OceanBloom.hlsl");
            _extract = Own(device.CreatePixelShader(Compiler.CompileFromFile(path,"ExtractPS","ps_5_0").Span));
            _blur = Own(device.CreatePixelShader(Compiler.CompileFromFile(path,"BlurPS","ps_5_0").Span));
            _constants = Own(device.CreateBuffer(16,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write));
            for(var i=0;i<2;i++)
            {
                var texture = Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,(uint)Width,(uint)Height,1,1,
                    BindFlags.RenderTarget|BindFlags.ShaderResource)));
                _targets[i]=Own(device.CreateRenderTargetView(texture)); _reads[i]=Own(device.CreateShaderResourceView(texture));
            }
        }
        catch { Dispose(); throw; }
    }
    private unsafe void Constants(Vector4 value)
    {
        var mapped=_context.Map(_constants,0,MapMode.WriteDiscard);
        try { *(Vector4*)mapped.DataPointer=value; } finally { _context.Unmap(_constants,0); }
        _context.PSSetConstantBuffer(1,_constants);
    }
    public void Render(ID3D11ShaderResourceView scene, float exposure)
    {
        _context.PSSetShaderResource(14,null!);
        _context.RSSetViewport(new Viewport(0,0,Width,Height));
        _context.OMSetRenderTargets(_targets[0]);
        Constants(new(1f/Width,1f/Height,exposure,0));
        _context.PSSetShaderResource(0,scene); _context.PSSetShader(_extract); _context.Draw(3,0);
        _context.PSSetShaderResource(0,null!);
        _context.OMSetRenderTargets(_targets[1]);
        Constants(new(1f/Width,0,0,0));
        _context.PSSetShaderResource(0,_reads[0]); _context.PSSetShader(_blur); _context.Draw(3,0);
        _context.PSSetShaderResource(0,null!);
        _context.OMSetRenderTargets(_targets[0]);
        Constants(new(0,1f/Height,0,0));
        _context.PSSetShaderResource(0,_reads[1]); _context.Draw(3,0);
        _context.PSSetShaderResource(0,null!);
        _context.OMSetRenderTargets((ID3D11RenderTargetView)null!);
    }
    public void Dispose() { for(var i=_owned.Count-1;i>=0;i--) _owned[i].Dispose(); _owned.Clear(); }
}
