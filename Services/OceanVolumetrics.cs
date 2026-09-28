using System.IO;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AnimatedWallPaper.Services;

// Complete, deterministic snapshots. View radiance, incident probes, finite S/T and
// spatial light transmission are separate consumers of the same world-space medium.
internal sealed class OceanVolumetrics : IDisposable
{
    internal sealed record Profile(int Width,int Height,int CloudSteps,int AirSteps,int FogSteps,int ShadowSteps,
        int AerialWidth,int AerialHeight,int AerialDepth,int LightSize,int ProbeWidth,int ProbeHeight);
    internal static Profile For(OceanQuality quality) => quality switch
    {
        OceanQuality.Economy => new(768,192,24,12,8,8,64,32,32,64,128,64),
        OceanQuality.High => new(3072,768,64,28,20,20,192,96,64,192,384,192),
        _ => new(1536,384,40,20,12,12,128,64,48,128,256,128)
    };
    internal sealed record Volume(ID3D11Texture3D Texture,ID3D11ShaderResourceView Read,ID3D11UnorderedAccessView Write);
    private sealed record ProbeArray(ID3D11ShaderResourceView Read,ID3D11UnorderedAccessView Write);
    private readonly List<IDisposable> _owned=[];
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11ComputeShader _noiseShader,_skyShader,_aerialShader,_lightShader,_reflectionShader;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11SamplerState _wrap,_clamp;
    private readonly Volume _noise;
    private readonly ID3D11ShaderResourceView[] _skyRead=new ID3D11ShaderResourceView[3];
    private readonly ID3D11UnorderedAccessView[] _skyWrite=new ID3D11UnorderedAccessView[3];
    internal Volume[] Scatter { get; }=new Volume[3];
    internal Volume[] Transmit { get; }=new Volume[3];
    internal Volume[] Light { get; }=new Volume[3];
    private readonly ProbeArray[] _reflection=new ProbeArray[3];
    private readonly long[] _ticks=[long.MinValue,long.MinValue,long.MinValue];
    private (OceanWeatherSettings,OceanCelestialSettings,bool)? _key;
    private DateTimeOffset _epoch;
    private int _first,_second,_preparingIndex=-1,_preparingStage;
    private long _preparingTick=long.MinValue;
    private double _lastWeatherTime=double.NaN;
    public OceanQuality Quality { get; }
    public Profile Budget { get; }
    public float Blend { get; private set; }
    public int BuildCount { get; private set; }
    public int WorkCount { get; private set; }
    public long EstimatedBytes { get; private set; }
    internal int CurrentIndex => _first;
    private T Own<T>(T value) where T:IDisposable { _owned.Add(value); return value; }
    private Volume CreateVolume(int width,int height,int depth,Format format,int bytes,bool mipmaps=false)
    {
        var description=new Texture3DDescription(format,(uint)width,(uint)height,(uint)depth,mipmaps ? 0u : 1u,
            BindFlags.ShaderResource|BindFlags.UnorderedAccess|(mipmaps ? BindFlags.RenderTarget : BindFlags.None));
        if(mipmaps) description.MiscFlags=ResourceOptionFlags.GenerateMips;
        var texture=Own(_device.CreateTexture3D(description));
        EstimatedBytes+=(long)width*height*depth*bytes;
        if(mipmaps) EstimatedBytes+=(long)width*height*depth*bytes/7;
        return new(texture,Own(_device.CreateShaderResourceView(texture)),Own(_device.CreateUnorderedAccessView(texture)));
    }
    public OceanVolumetrics(ID3D11Device device,ID3D11DeviceContext context,OceanQuality quality)
    {
        _device=device; _context=context; Quality=quality; Budget=For(quality);
        try
        {
            var path=Path.Combine(AppContext.BaseDirectory,"Shaders","OceanVolume.hlsl");
            ID3D11ComputeShader Shader(string entry)=>Own(device.CreateComputeShader(OceanShader.Compile(path,entry,"cs_5_0").Span));
            _noiseShader=Shader("BuildNoise"); _lightShader=Shader("BuildLight"); _skyShader=Shader("BuildEnvironment");
            _aerialShader=Shader("BuildAerial"); _reflectionShader=Shader("BuildReflections");
            _constants=Own(device.CreateBuffer(12*16,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write));
            _wrap=Own(device.CreateSamplerState(new SamplerDescription {Filter=Filter.MinMagMipLinear,
                AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Wrap,MaxLOD=float.MaxValue}));
            _clamp=Own(device.CreateSamplerState(new SamplerDescription {Filter=Filter.MinMagMipLinear,
                AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaxLOD=float.MaxValue}));
            _noise=CreateVolume(64,64,64,Format.R16G16_Float,4,true);
            for(var i=0;i<3;i++)
            {
                var sky=Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,(uint)Budget.Width,(uint)Budget.Height,
                    1,0,BindFlags.ShaderResource|BindFlags.UnorderedAccess|BindFlags.RenderTarget,miscFlags:ResourceOptionFlags.GenerateMips)));
                _skyRead[i]=Own(device.CreateShaderResourceView(sky)); _skyWrite[i]=Own(device.CreateUnorderedAccessView(sky));
                EstimatedBytes+=(long)(Budget.Width*Budget.Height*8*4d/3);
                Scatter[i]=CreateVolume(Budget.AerialWidth,Budget.AerialHeight,Budget.AerialDepth,Format.R16G16B16A16_Float,8);
                Transmit[i]=CreateVolume(Budget.AerialWidth,Budget.AerialHeight,Budget.AerialDepth,Format.R16G16B16A16_Float,8);
                Light[i]=CreateVolume(Budget.LightSize,Budget.LightSize,16,Format.R16G16_Float,4);
                var probes=Own(device.CreateTexture2D(new Texture2DDescription(Format.R16G16B16A16_Float,(uint)Budget.ProbeWidth,(uint)Budget.ProbeHeight,
                    9,0,BindFlags.ShaderResource|BindFlags.UnorderedAccess|BindFlags.RenderTarget,miscFlags:ResourceOptionFlags.GenerateMips)));
                _reflection[i]=new(Own(device.CreateShaderResourceView(probes)),Own(device.CreateUnorderedAccessView(probes)));
                EstimatedBytes+=(long)(Budget.ProbeWidth*Budget.ProbeHeight*9*8*4d/3);
            }
            context.CSSetShader(_noiseShader); context.CSSetUnorderedAccessView(4,_noise.Write);
            context.Dispatch(16,16,16); context.CSSetUnorderedAccessView(4,null!); context.GenerateMips(_noise.Read); context.CSSetShader(null!);
        }
        catch { Dispose(); throw; }
    }
    public void Update(double weatherTime,OceanSettings settings,OceanCelestialFrame frame)
    {
        weatherTime=double.IsFinite(weatherTime) ? Math.Max(0,weatherTime) : 0;
        var key=(settings.Weather!,frame.Settings,settings.Horizon);
        var epoch=frame.Utc.AddSeconds(-weatherTime*frame.Settings.TimeScale);
        if(_key!=key || Math.Abs((epoch-_epoch).TotalSeconds)>.05)
        {
            _key=key; _epoch=epoch; Array.Fill(_ticks,long.MinValue);
            _preparingIndex=-1; _lastWeatherTime=double.NaN;
        }
        // Present two complete samples; prepare a third in four independent passes.
        // Missing samples after seek/recreation are built synchronously, so scheduling
        // changes latency only, never the image at a given simulation time.
        var interval=Quality==OceanQuality.Economy ? 1d : .5d;
        var value=weatherTime/interval; var tick=(long)Math.Floor(value); Blend=(float)(value-tick);
        _context.CSSetShaderResource(23,_noise.Read); _context.CSSetSampler(4,_wrap); _context.CSSetSampler(5,_clamp);
        _first=Array.IndexOf(_ticks,tick);
        if(_first<0)
        {
            _first=Array.FindIndex(_ticks,t=>t!=tick+1);
            BuildComplete(_first,tick,interval,settings);
        }
        _second=Array.IndexOf(_ticks,tick+1);
        if(_second<0)
        {
            _second=(_first+1)%3;
            BuildComplete(_second,tick+1,interval,settings);
        }
        var spare=3-_first-_second;
        if(_ticks[spare]!=tick+2 && weatherTime!=_lastWeatherTime)
        {
            if(_preparingIndex!=spare || _preparingTick!=tick+2)
            {
                _preparingIndex=spare; _preparingTick=tick+2; _preparingStage=0;
                _ticks[spare]=long.MinValue;
            }
            if(Blend>=_preparingStage*.2f)
                BuildPass(spare,tick+2,interval,settings,_preparingStage++);
        }
        _lastWeatherTime=weatherTime;
        _context.CSSetShaderResource(23,null!); _context.CSSetShaderResource(24,null!);
    }
    private void BuildComplete(int index,long tick,double interval,OceanSettings settings)
    {
        if(_preparingIndex==index) _preparingIndex=-1;
        for(var pass=0;pass<4;pass++) BuildPass(index,tick,interval,settings,pass);
    }
    private unsafe void BuildPass(int index,long tick,double interval,OceanSettings settings,int pass)
    {
        var weather=settings.Weather!; var time=tick*interval;
        var frame=OceanCelestialModel.Evaluate(_epoch.AddSeconds(time*_key!.Value.Item2.TimeScale),_key.Value.Item2);
        var layer=weather.Layer; var fog=weather.FogProfile;
        var mapped=_context.Map(_constants,0,MapMode.WriteDiscard);
        try
        {
            var data=new Span<Vector4>((void*)mapped.DataPointer,12); data.Clear();
            data[0]=new(frame.Sun.Direction,frame.Sun.Radius); data[1]=new(frame.Sun.Irradiance,OceanOpticalDepth.Aerosol(frame.Settings.Air));
            data[2]=new(frame.Moon.Direction,frame.Moon.Radius); data[3]=new(frame.Moon.Irradiance,0);
            data[4]=new(layer.Base,layer.Top,weather.Coverage,(int)weather.Clouds);
            data[5]=new(fog.Extinction,fog.Height,fog.Banks,5);
            // Common lattice period for .22/.7/1.15/2.3/7.5 noise frequencies is 1600 km.
            data[6]=new((float)(time*weather.WindMetresPerSecond*.001%1600),
                (float)(time*weather.WindMetresPerSecond*.000333333333%1600),weather.Seed*.37f,0);
            data[7]=new(0,settings.Horizon ? .0028f : .0017f,-.004f,frame.Daylight);
            data[8]=new(Budget.Width,Budget.Height,Budget.CloudSteps,Budget.ShadowSteps);
            data[9]=new(Budget.AirSteps,Budget.FogSteps,Budget.AerialDepth,Budget.LightSize);
            data[10]=new(32,16,layer.Top,Quality==OceanQuality.High ? 3 : 2);
        }
        finally { _context.Unmap(_constants,0); }
        _context.CSSetConstantBuffer(1,_constants);
        _context.CSSetShaderResource(24,pass==0 ? null! : Light[index].Read);
        switch(pass)
        {
            case 0:
                _context.CSSetShader(_lightShader); _context.CSSetUnorderedAccessView(3,Light[index].Write);
                _context.Dispatch((uint)Budget.LightSize/8,(uint)Budget.LightSize/8,16);
                _context.CSSetUnorderedAccessView(3,null!);
                break;
            case 1:
                _context.CSSetShader(_skyShader); _context.CSSetUnorderedAccessView(0,_skyWrite[index]);
                _context.Dispatch((uint)Budget.Width/8,(uint)Budget.Height/8,1);
                _context.CSSetUnorderedAccessView(0,null!); _context.GenerateMips(_skyRead[index]);
                break;
            case 2:
                _context.CSSetShader(_aerialShader); _context.CSSetUnorderedAccessView(1,Scatter[index].Write);
                _context.CSSetUnorderedAccessView(2,Transmit[index].Write);
                _context.Dispatch((uint)Budget.AerialWidth/8,(uint)Budget.AerialHeight/8,1);
                _context.CSSetUnorderedAccessView(1,null!); _context.CSSetUnorderedAccessView(2,null!);
                break;
            case 3:
                _context.CSSetShader(_reflectionShader); _context.CSSetUnorderedAccessView(5,_reflection[index].Write);
                _context.Dispatch((uint)Budget.ProbeWidth/8,(uint)Budget.ProbeHeight/8,9);
                _context.CSSetUnorderedAccessView(5,null!); _context.GenerateMips(_reflection[index].Read);
                _ticks[index]=tick; BuildCount++;
                break;
        }
        WorkCount++;
        _context.CSSetShader(null!);
    }
    public void Bind()
    {
        _context.PSSetShaderResource(18,_skyRead[_first]); _context.PSSetShaderResource(19,_skyRead[_second]);
        _context.PSSetShaderResource(20,Scatter[_first].Read); _context.PSSetShaderResource(21,Scatter[_second].Read);
        _context.PSSetShaderResource(22,Transmit[_first].Read); _context.PSSetShaderResource(23,Transmit[_second].Read);
        _context.PSSetShaderResource(24,Light[_first].Read); _context.PSSetShaderResource(25,Light[_second].Read);
        _context.PSSetShaderResource(26,_reflection[_first].Read); _context.PSSetShaderResource(27,_reflection[_second].Read);
    }
    public void Dispose() { for(var i=_owned.Count-1;i>=0;i--) _owned[i].Dispose(); _owned.Clear(); }
}
