using System.IO;
using System.Numerics;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class OceanCycleSky : IDisposable
{
    private readonly List<IDisposable> _owned=[];
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11ComputeShader _shader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11ShaderResourceView[] _read=new ID3D11ShaderResourceView[2];
    private readonly ID3D11UnorderedAccessView[] _write=new ID3D11UnorderedAccessView[2];
    private readonly long[] _ticks=[long.MinValue,long.MinValue];
    private OceanCelestialSettings? _key;
    private int _first;
    public int Width { get; }
    public int Height { get; }
    public OceanQuality Quality { get; }
    public float Blend { get; private set; }
    public int BuildCount { get; private set; }
    public ID3D11ShaderResourceView Previous => _read[_first];
    public ID3D11ShaderResourceView Next => _read[1-_first];
    public long EstimatedBytes => (long)(Width*Height*8*2*4d/3);
    private T Own<T>(T value) where T:IDisposable { _owned.Add(value); return value; }
    public OceanCycleSky(ID3D11Device device, ID3D11DeviceContext context, OceanQuality quality)
    {
        _context=context; Quality=quality;
        (Width,Height)=quality switch { OceanQuality.Economy=>(512,128), OceanQuality.High=>(2048,512), _=>(1024,256) };
        try
        {
            _shader=Own(device.CreateComputeShader(OceanShader.Compile(Path.Combine(AppContext.BaseDirectory,"Shaders","OceanCycleSky.hlsl"),"BuildCycleSky","cs_5_0").Span));
            _constants=Own(device.CreateBuffer(96,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write));
            for(var i=0;i<2;i++)
            {
                var texture=Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,(uint)Width,(uint)Height,1,0,
                    BindFlags.ShaderResource|BindFlags.UnorderedAccess|BindFlags.RenderTarget,miscFlags:ResourceOptionFlags.GenerateMips)));
                _read[i]=Own(device.CreateShaderResourceView(texture)); _write[i]=Own(device.CreateUnorderedAccessView(texture));
            }
        }
        catch { Dispose(); throw; }
    }
    public void Update(OceanCelestialFrame frame)
    {
        if(_key!=frame.Settings) { _ticks[0]=_ticks[1]=long.MinValue; _key=frame.Settings; }
        // At most two builds per seek; continuous playback reuses the previously computed next snapshot.
        var interval=Math.Max(1,frame.Settings.TimeScale*(Quality==OceanQuality.Economy ? 1 : .5));
        var value=(frame.Utc-DateTimeOffset.UnixEpoch).TotalSeconds/interval;
        var tick=(long)Math.Floor(value); Blend=(float)(value-tick);
        _first=_ticks[1]==tick ? 1 : _ticks[0]==tick ? 0 : _ticks[0]==tick+1 ? 1 : 0;
        if(_ticks[_first]!=tick) Build(_first,tick,interval,frame.Settings);
        if(_ticks[1-_first]!=tick+1) Build(1-_first,tick+1,interval,frame.Settings);
    }
    private unsafe void Build(int index,long tick,double interval,OceanCelestialSettings settings)
    {
        var frame=OceanCelestialModel.Evaluate(DateTimeOffset.UnixEpoch.AddSeconds(tick*interval),settings);
        var mapped=_context.Map(_constants,0,MapMode.WriteDiscard);
        try
        {
            var data=new Span<Vector4>((void*)mapped.DataPointer,6);
            data[0]=new(frame.Sun.Direction,0); data[1]=new(frame.Sun.Irradiance,0);
            data[2]=new(frame.Moon.Direction,0); data[3]=new(frame.Moon.Irradiance,0);
            data[4]=new(Width,Height,Quality==OceanQuality.Economy ? 16 : 32,OceanOpticalDepth.Aerosol(settings.Air));
            data[5]=new(frame.Daylight,0,0,0);
        }
        finally { _context.Unmap(_constants,0); }
        _context.CSSetShader(_shader); _context.CSSetConstantBuffer(0,_constants);
        _context.CSSetUnorderedAccessView(0,_write[index]); _context.Dispatch((uint)(Width/8),(uint)(Height/8),1);
        _context.CSSetUnorderedAccessView(0,null!); _context.CSSetShader(null!);
        _context.GenerateMips(_read[index]); _ticks[index]=tick; BuildCount++;
    }
    public void Dispose() { for(var i=_owned.Count-1;i>=0;i--) _owned[i].Dispose(); _owned.Clear(); }
}
