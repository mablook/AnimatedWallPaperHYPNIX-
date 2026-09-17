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

internal sealed class FireSimulation : IDisposable
{
    internal const int Width=1080, Height=640, X=288, Y=128, Z=40;
    internal const int SpeedMultiplier=4;
    readonly List<IDisposable> owned=[];
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly ID3D11VertexShader vs;
    readonly ID3D11PixelShader ps;
    readonly ID3D11PixelShader composite;
    readonly ID3D11VertexShader sparksVs;
    readonly ID3D11PixelShader sparksPs;
    readonly ID3D11BlendState sparkBlend;
    readonly ID3D11RasterizerState rasterizer;
    readonly ID3D11Buffer constants;
    readonly ID3D11SamplerState sampler;
    readonly Dictionary<string,ID3D11ComputeShader> shaders=[];
    readonly Volume[] velocities, materials, pressures;
    readonly Volume div, curl, transported;
    readonly ParticleField[] particles;
    int v,m,p,s,frame;
    public float Strength { get; set; }=1;
    public bool SparksVisible { get; set; }=true;
    public bool FireBed { get; set; }=true;
    public Vector3 AudioTarget { get; set; }
    public Vector3 AudioEnvelope { get; private set; }
    readonly float[] frequencyTargets=new float[FireEmitterLayout.MaxCount];
    readonly float[] frequencyEnvelope=new float[FireEmitterLayout.MaxCount];
    public int FlameCount { get; }
    bool hasSpectrum;
    public void UseGroupedAudio()=>hasSpectrum=false;
    public void SetFrequencyTargets(ReadOnlySpan<float> values)
    {
        if(values.Length!=FlameCount)throw new ArgumentException("Expected one frequency group per flame.",nameof(values));
        values.CopyTo(frequencyTargets);hasSpectrum=true;
    }
    public double Time => frame/60.0;
    public Vector4 Layout { get; set; }=new(1,0,0,1);
    public Vector4 PaletteLow { get; set; }=new(1,.1f,0,0);
    public Vector4 PaletteHigh { get; set; }=new(1,.6f,.1f,0);
    public Vector4 BackgroundColor { get; set; }
    public Vector4 BackgroundSize { get; set; }
    int renderWidth=Width, renderHeight=Height;
    T Own<T>(T value) where T:IDisposable { owned.Add(value); return value; }
    record Volume(ID3D11Texture3D Texture, ID3D11ShaderResourceView Read, ID3D11UnorderedAccessView Write);
    record ParticleField(ID3D11Texture2D Texture, ID3D11ShaderResourceView Read, ID3D11UnorderedAccessView Write);

    public FireSimulation(ID3D11Device device, ID3D11DeviceContext context,int flameCount=FireFrequencyBands.Count)
    {
        if(flameCount<1||flameCount>FireEmitterLayout.MaxCount)throw new ArgumentOutOfRangeException(nameof(flameCount));
        FlameCount=flameCount;
        this.device=device;this.context=context;
        try {
            var path=Path.Combine(AppContext.BaseDirectory,"Shaders","LivingFire.hlsl");
            vs=Own(device.CreateVertexShader(Compiler.CompileFromFile(path,"VSMain","vs_5_0").Span));
            ps=Own(device.CreatePixelShader(Compiler.CompileFromFile(path,"PSMain","ps_5_0").Span));
            composite=Own(device.CreatePixelShader(Compiler.CompileFromFile(path,"CompositePS","ps_5_0").Span));
            sparksVs=Own(device.CreateVertexShader(Compiler.CompileFromFile(path,"SparksVS","vs_5_0").Span));
            sparksPs=Own(device.CreatePixelShader(Compiler.CompileFromFile(path,"SparksPS","ps_5_0").Span));
            sparkBlend=Own(device.CreateBlendState(new BlendDescription(Blend.One,Blend.One)));
            rasterizer=Own(device.CreateRasterizerState(RasterizerDescription.CullNone));
            foreach(var name in new[]{"Transport","Advect","Curl","Confine","Diverge","Jacobi","Project","Sparks"})
                shaders[name]=Own(device.CreateComputeShader(Compiler.CompileFromFile(path,name,"cs_5_0").Span));
            constants=Own(device.CreateBuffer(336,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write));
            sampler=Own(device.CreateSamplerState(new SamplerDescription {
                Filter=Filter.MinMagMipLinear, AddressU=TextureAddressMode.Clamp, AddressV=TextureAddressMode.Clamp,
                AddressW=TextureAddressMode.Clamp, MaxLOD=float.MaxValue, MaxAnisotropy=1 }));
            velocities=[CreateVolume(),CreateVolume()]; materials=[CreateVolume(),CreateVolume()];
            pressures=[CreateVolume(true),CreateVolume(true)]; div=CreateVolume(true); curl=CreateVolume();
            transported=CreateVolume();particles=[CreateParticles(),CreateParticles()];
            Reset();
        } catch { Dispose(); throw; }
    }
    ParticleField CreateParticles() {
        var tex=Own(device.CreateTexture2D(new Texture2DDescription(Format.R32G32B32A32_Float,64,2,1,1,
            BindFlags.ShaderResource|BindFlags.UnorderedAccess)));
        return new(tex,Own(device.CreateShaderResourceView(tex)),Own(device.CreateUnorderedAccessView(tex)));
    }
    Volume CreateVolume(bool scalar=false) {
        var tex=Own(device.CreateTexture3D(new Texture3DDescription(scalar?Format.R32_Float:Format.R16G16B16A16_Float,
            X,Y,Z,1,BindFlags.ShaderResource|BindFlags.UnorderedAccess)));
        return new(tex,Own(device.CreateShaderResourceView(tex)),Own(device.CreateUnorderedAccessView(tex)));
    }
    public void Reset() {
        foreach(var field in velocities.Concat(materials).Concat(pressures).Append(div).Append(curl).Append(transported))
            context.ClearUnorderedAccessView(field.Write,Vector4.Zero);
        foreach(var field in particles)context.ClearUnorderedAccessView(field.Write,Vector4.Zero);
        v=m=p=s=frame=0;
        AudioEnvelope=Vector3.Zero;
        Array.Clear(frequencyEnvelope);
    }
    unsafe void SetConstants() {
        var map=context.Map(constants,0,MapMode.WriteDiscard);
        var data=(Vector4*)map.DataPointer;
        data[0]=new(X,Y,Z,1f/60); data[1]=new(renderWidth,renderHeight,(float)Time,Strength);
        data[2]=new(FireBed?1:0,AudioEnvelope.X,AudioEnvelope.Y,AudioEnvelope.Z);
        data[3]=Layout;data[4]=PaletteLow;data[5]=PaletteHigh;
        for(int i=0;i<FireEmitterLayout.MaxCount/4;i++)
            data[6+i]=new(frequencyEnvelope[i*4],frequencyEnvelope[i*4+1],frequencyEnvelope[i*4+2],frequencyEnvelope[i*4+3]);
        data[18]=new(FlameCount,FireEmitterLayout.EdgePadding,0,0);
        data[19]=BackgroundColor;data[20]=BackgroundSize;
        context.Unmap(constants,0);
    }
    void Run(string name, params (uint slot,Volume volume)[] outputs) {
        context.CSSetShader(shaders[name]); context.CSSetConstantBuffer(0,constants); context.CSSetSampler(0,sampler);
        context.CSSetShaderResource(0,velocities[v].Read); context.CSSetShaderResource(1,materials[m].Read);
        context.CSSetShaderResource(2,pressures[p].Read);
        if(name!="Diverge")context.CSSetShaderResource(3,div.Read);
        if(name!="Curl")context.CSSetShaderResource(4,curl.Read);
        if(name=="Advect")context.CSSetShaderResource(6,transported.Read);
        foreach(var (slot,volume) in outputs)context.CSSetUnorderedAccessView(slot,volume.Write);
        context.Dispatch((X+7)/8,(Y+7)/8,(Z+3)/4);
        for(uint i=0;i<5;i++) {context.CSSetUnorderedAccessView(i,null); context.CSSetShaderResource(i,null);}
        context.CSSetShaderResource(6,null);
    }
    public (double before,double after)? Step(bool measure=false) {
        // Four stable 1/60 physics substeps per logical tick: four times the
        // original flame/ember motion, without increasing the solver timestep.
        for(int substep=0;substep<SpeedMultiplier-1;substep++)StepCore(false);
        return StepCore(measure);
    }
    (double before,double after)? StepCore(bool measure) {
        AudioEnvelope=new(Smooth(AudioEnvelope.X,AudioTarget.X),Smooth(AudioEnvelope.Y,AudioTarget.Y),Smooth(AudioEnvelope.Z,AudioTarget.Z));
        for(int i=0;i<FlameCount;i++)
            frequencyEnvelope[i]=Smooth(frequencyEnvelope[i],hasSpectrum?frequencyTargets[i]:i<FlameCount/3?AudioTarget.Z:i<FlameCount*2/3?AudioTarget.Y:AudioTarget.X);
        SetConstants();
        Run("Transport",(1,transported));
        Run("Advect",(0,velocities[1-v]),(1,materials[1-m])); v=1-v; m=1-m;
        Run("Curl",(4,curl)); Run("Confine",(0,velocities[1-v])); v=1-v;
        Run("Diverge",(3,div)); double before=measure?ReadDivergence():0;
        for(int i=0;i<24;i++) {Run("Jacobi",(2,pressures[1-p]));p=1-p;}
        Run("Project",(0,velocities[1-v]));v=1-v;
        double after=0;
        if(measure) {Run("Diverge",(3,div));after=ReadDivergence();}
        context.CSSetShader(shaders["Sparks"]);
        context.CSSetShaderResource(0,velocities[v].Read);context.CSSetShaderResource(5,particles[s].Read);
        context.CSSetUnorderedAccessView(5,particles[1-s].Write);context.Dispatch(1,1,1);
        context.CSSetUnorderedAccessView(5,null);context.CSSetShaderResource(0,null);context.CSSetShaderResource(5,null);
        s=1-s;
        frame++;
        return measure?(before,after):null;
    }
    static float Smooth(float previous,float target) {
        target=float.IsFinite(target)?Math.Clamp(target,0,1):0;
        return previous+(target-previous)*(1-MathF.Exp(-(target>previous?12f:3f)/(60f*SpeedMultiplier)));
    }
    public void Render(int width,int height) {
        renderWidth=width;renderHeight=height;
        SetConstants(); context.CSSetShader(null!);
        context.RSSetViewport(new Viewport(0,0,width,height));
        context.RSSetState(rasterizer);
        context.OMSetBlendState(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(vs); context.PSSetShader(ps); context.PSSetConstantBuffer(0,constants);
        context.PSSetShaderResource(1,materials[m].Read); context.PSSetSampler(0,sampler);
        context.Draw(3,0);
        if(SparksVisible) {
            context.VSSetShader(sparksVs);context.VSSetConstantBuffer(0,constants);context.VSSetShaderResource(5,particles[s].Read);
            context.PSSetShader(sparksPs);context.OMSetBlendState(sparkBlend);context.DrawInstanced(6,64,0,0);
            context.VSSetShaderResource(5,null);context.OMSetBlendState(null);
        }
        context.PSSetShaderResource(1,null!);

    }
    public Vector4[] ReadParticles() {
        var desc=particles[s].Texture.Description;desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;
        desc.CPUAccessFlags=CpuAccessFlags.Read;desc.MiscFlags=ResourceOptionFlags.None;
        using var staging=device.CreateTexture2D(desc);context.CopyResource(staging,particles[s].Texture);
        var map=context.Map(staging,0,MapMode.Read);var data=new float[64*2*4];
        try {for(int y=0;y<2;y++)Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,data,y*64*4,64*4);}
        finally {context.Unmap(staging,0);}
        if(data.Any(f=>!float.IsFinite(f)))throw new InvalidOperationException("Non-finite spark state");
        return Enumerable.Range(0,128).Select(i=>new Vector4(data[i*4],data[i*4+1],data[i*4+2],data[i*4+3])).ToArray();
    }
    public void Composite(ID3D11ShaderResourceView image, Viewport viewport, ID3D11ShaderResourceView? background=null) {
        context.RSSetViewport(viewport);context.VSSetShader(vs);context.PSSetShader(composite);
        context.PSSetShaderResource(7,image);context.PSSetSampler(0,sampler);context.PSSetConstantBuffer(0,constants);
        context.PSSetShaderResource(8,background!);
        context.Draw(3,0);context.PSSetShaderResource(7,null!);context.PSSetShaderResource(8,null!);
    }
    double ReadDivergence() {
        var data=ReadVolume(div,4); double total=0;
        for(int i=0;i<data.Length;i+=4) {var value=BitConverter.ToSingle(data,i); if(!float.IsFinite(value))throw new InvalidOperationException("Non-finite divergence");total+=value*value;}
        return Math.Sqrt(total/(X*Y*Z));
    }
    public (double heat,double fuel,int active) Inspect() {
        var data=ReadVolume(materials[m],8);double heat=0,fuel=0;int active=0;
        for(int i=0;i<data.Length;i+=8) {
            for(int c=0;c<4;c++) {var f=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,i+c*2));
                if(!float.IsFinite(f)||f<0)throw new InvalidOperationException("Invalid material field");
                if(c==0)fuel+=f;if(c==1){heat+=f;if(f>.1)active++;}}
        }
        return(heat,fuel,active);
    }
    public double[] HeatByZone() {
        var data=ReadVolume(materials[m],8);var zones=new double[9];
        for(int cell=0;cell<X*Y*Z;cell++)
            zones[(cell%X)*9/X]+=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,cell*8+2));
        return zones;
    }
    byte[] ReadVolume(Volume volume,int size) {
        var desc=volume.Texture.Description;desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;
        desc.CPUAccessFlags=CpuAccessFlags.Read;desc.MiscFlags=ResourceOptionFlags.None;
        using var staging=device.CreateTexture3D(desc);context.CopyResource(staging,volume.Texture);
        var map=context.Map(staging,0,MapMode.Read);var data=new byte[X*Y*Z*size];
        try {for(int z=0;z<Z;z++)for(int y=0;y<Y;y++)Marshal.Copy(map.DataPointer+z*(int)map.DepthPitch+y*(int)map.RowPitch,data,(z*Y+y)*X*size,X*size);}
        finally {context.Unmap(staging,0);}return data;
    }
    public void Dispose() {for(int i=owned.Count-1;i>=0;i--)owned[i].Dispose();owned.Clear();}
}
