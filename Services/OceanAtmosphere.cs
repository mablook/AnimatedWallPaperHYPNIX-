using System.IO;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class OceanAtmosphere : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11ComputeShader _shader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11UnorderedAccessView _write;
    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView Read { get; }
    public ID3D11SamplerState Sampler { get; }
    public int BuildCount { get; private set; }
    private (OceanLighting, bool, bool)? _current;
    private T Own<T>(T value) where T : IDisposable { _owned.Add(value); return value; }
    public OceanAtmosphere(ID3D11Device device, ID3D11DeviceContext context)
    {
        _context=context;
        try
        {
            _shader=Own(device.CreateComputeShader(Compiler.CompileFromFile(Path.Combine(AppContext.BaseDirectory,"Shaders","OceanAtmosphere.hlsl"),"BuildSky","cs_5_0").Span));
            _constants=Own(device.CreateBuffer(48,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write));
            Texture=Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,2048,512,1,0,
                BindFlags.ShaderResource|BindFlags.UnorderedAccess|BindFlags.RenderTarget,miscFlags:ResourceOptionFlags.GenerateMips)));
            Read=Own(device.CreateShaderResourceView(Texture)); _write=Own(device.CreateUnorderedAccessView(Texture));
            Sampler=Own(device.CreateSamplerState(new SamplerDescription
            {
                Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Clamp,
                AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue,MaxAnisotropy=1
            }));
        }
        catch { Dispose(); throw; }
    }
    public unsafe void Update(OceanLighting light, bool clearOnly = false, bool gibbous = false)
    {
        var key = (light, clearOnly, gibbous);
        if (_current==key) return;
        var preset=OceanLightingModel.For(light,true);
        var mapped=_context.Map(_constants,0,MapMode.WriteDiscard);
        try
        {
            var data=new Span<Vector4>((void*)mapped.DataPointer,3);
            data[0]=new(preset.Direction,preset.CloudCover);
            data[1]=new(preset.Irradiance * (clearOnly && gibbous && preset.Night ? OceanLightingModel.GibbousEnergy : 1),preset.Night?1:0);
            data[2]=new(light==OceanLighting.Overcast?1:0,clearOnly?1:0,0,0);
        }
        finally { _context.Unmap(_constants,0); }
        _context.PSSetShaderResource(10,null!);
        _context.CSSetConstantBuffer(0,_constants); _context.CSSetShader(_shader);
        _context.CSSetUnorderedAccessView(0,_write); _context.Dispatch(256,64,1);
        _context.CSSetUnorderedAccessView(0,null!); _context.CSSetShader(null!);
        _context.GenerateMips(Read); _current=key; BuildCount++;
    }
    public void Dispose() { for(var i=_owned.Count-1;i>=0;i--) _owned[i].Dispose(); _owned.Clear(); }
}
