using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace AnimatedWallPaper.Services;

internal sealed class FireGpuRenderer : IDisposable
{
    readonly List<IDisposable> owned=[];
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly IDXGISwapChain1 swap;
    readonly ID3D11Texture2D back;
    readonly ID3D11RenderTargetView target;
    readonly Dictionary<(int,int,int,int),Surface> surfaces=[];
    readonly int width,height;
    public string AdapterName { get; }
    T Own<T>(T value) where T:IDisposable {owned.Add(value);return value;}

    sealed class Surface : IDisposable
    {
        public readonly FireSimulation Simulation;
        public readonly FireBackground Background;
        public readonly FireFrameStepper Clock=new();
        public readonly ID3D11Texture2D Image;
        public readonly ID3D11RenderTargetView Target;
        public readonly ID3D11ShaderResourceView Read;
        public readonly int Width,Height;
        public Surface(ID3D11Device device,ID3D11DeviceContext context,int width,int height) {
            // Bound shading cost independently of desktop pixel count. Aspect ratio is preserved.
            double ratio=Math.Min(1,Math.Min(1080d/width,720d/height));
            Width=Math.Max(1,(int)Math.Round(width*ratio));Height=Math.Max(1,(int)Math.Round(height*ratio));
            try {
                Background=new(device);
                Simulation=new(device,context,FireEmitterLayout.CountForViewport(width,height));
                Image=device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,(uint)Width,(uint)Height,1,1,BindFlags.RenderTarget|BindFlags.ShaderResource));
                Target=device.CreateRenderTargetView(Image);Read=device.CreateShaderResourceView(Image);
            } catch {Dispose();throw;}
        }
        public void Dispose(){Read?.Dispose();Target?.Dispose();Image?.Dispose();Simulation?.Dispose();Background?.Dispose();}
    }
    public FireGpuRenderer(IntPtr hwnd,int width,int height) {
        this.width=width;this.height=height;
        try {
            var factory=Own(CreateDXGIFactory1<IDXGIFactory2>());
            D3D11CreateDevice(null,DriverType.Hardware,DeviceCreationFlags.BgraSupport,new[]{FeatureLevel.Level_11_0},out device,out _,out context).CheckError();
            Own(device);Own(context);
            using(var dxgi=device.QueryInterface<IDXGIDevice>())
            using(var adapter=dxgi.GetAdapter())AdapterName=adapter.Description.Description;
            swap=Own(factory.CreateSwapChainForHwnd(device,hwnd,new SwapChainDescription1 {
                Width=(uint)width,Height=(uint)height,Format=Format.B8G8R8A8_UNorm,BufferCount=2,
                BufferUsage=Usage.RenderTargetOutput,SampleDescription=SampleDescription.Default,
                Scaling=Scaling.Stretch,SwapEffect=SwapEffect.FlipDiscard,AlphaMode=AlphaMode.Ignore
            },new SwapChainFullscreenDescription {Windowed=true}));
            factory.MakeWindowAssociation(hwnd,WindowAssociationFlags.IgnoreAltEnter);
            back=Own(swap.GetBuffer<ID3D11Texture2D>(0));target=Own(device.CreateRenderTargetView(back));
        } catch {Dispose();throw;}
    }
    Surface Get(int x,int y,int w,int h) {
        var key=(x,y,w,h);
        if(!surfaces.TryGetValue(key,out var surface)) {surface=new(device,context,w,h);surfaces.Add(key,surface);}
        return surface;
    }
    public FireSimulation GetSimulation(int x,int y,int w,int h)=>Get(x,y,w,h).Simulation;
    public void BeginFrame()=>context.ClearRenderTargetView(target,new Color4(0,0,0,1));
    public void RenderViewport(int x,int y,int w,int h,double time,AethelisAudioProfile audio,VisualizerSettings settings,bool frozen=false,float[]? spectrum=null) {
        bool created=!surfaces.ContainsKey((x,y,w,h));var surface=Get(x,y,w,h);var sim=surface.Simulation;
        if(!frozen||created) {
            sim.SetFlameCount(FireEmitterLayout.CountForViewport(w,h,settings.Scale));
            surface.Background.Update(settings.Background);
            sim.BackgroundColor=surface.Background.Color;sim.BackgroundSize=surface.Background.Size;
            sim.SparksVisible=settings.Sparks;
            sim.AudioTarget=new(audio.Bass,audio.Mids,audio.Highs);
            if(spectrum is not null)sim.SetFrequencyTargets(FireFrequencyBands.Analyze(spectrum,settings.Sensitivity,sim.FlameCount));
            else sim.UseGroupedAudio();
            sim.Strength=Math.Clamp(settings.Intensity/3,0,2.2f);
            sim.Layout=new(Math.Clamp(settings.Scale,.3f,3),Math.Clamp(settings.OffsetX,-1,1),Math.Clamp(settings.OffsetY,-1,1),settings.Intensity<=0?0:1);
            var low=settings.StartColor;var high=settings.EndColor;
            // The warm theme retains the approved incandescent palette; other themes tint it.
            bool natural=high.ToArgb()==System.Drawing.Color.FromArgb(255,185,70).ToArgb();
            sim.PaletteLow=new(low.R/255f,low.G/255f,low.B/255f,natural?0:1);
            sim.PaletteHigh=new(high.R/255f,high.G/255f,high.B/255f,Math.Clamp(settings.Glow,0,3));
        }
        if(created)for(int i=0;i<60;i++)sim.Step();
        int steps=surface.Clock.Advance(time,frozen);
        for(int i=0;i<steps;i++)sim.Step();
        RenderSurface(surface,new Viewport(x,y,w,h),!frozen||created);
    }
    void RenderSurface(Surface surface,Viewport viewport,bool update=true) {
        if(update){context.OMSetRenderTargets(surface.Target);surface.Simulation.Render(surface.Width,surface.Height);}
        context.OMSetRenderTargets(target);surface.Simulation.Composite(surface.Read,viewport,surface.Background.Image);
    }
    public void RenderPreview(bool present=true) {
        BeginFrame();var surface=Get(0,0,width,height);RenderSurface(surface,new Viewport(0,0,width,height));
        if(present)EndFrame();
    }
    public void EndFrame()=>swap.Present(0,PresentFlags.None).CheckError();
    public byte[] Pixels() {
        var desc=back.Description;desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.CPUAccessFlags=CpuAccessFlags.Read;desc.MiscFlags=ResourceOptionFlags.None;
        using var staging=device.CreateTexture2D(desc);context.CopyResource(staging,back);
        var map=context.Map(staging,0,MapMode.Read);var result=new byte[width*height*4];
        try {for(int y=0;y<height;y++)Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,result,y*width*4,width*4);}
        finally {context.Unmap(staging,0);}return result;
    }
    public void Dispose(){foreach(var surface in surfaces.Values)surface.Dispose();surfaces.Clear();for(int i=owned.Count-1;i>=0;i--)owned[i].Dispose();owned.Clear();}
}
