using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class FireRenderChecks
{
    public static void Run(IntPtr hwnd,string output)
    {
        var settings=new VisualizerPreferences(Glow:0,ColorTheme:1).ToSettings();
        var silent=new AethelisAudioProfile(0,0,0,0,0);
        using var r=new FireGpuRenderer(hwnd,640,360);
        void Frame(double t,VisualizerSettings? prefs=null) {r.BeginFrame();r.RenderViewport(0,0,640,360,t,silent,prefs??settings);}
        Frame(0);for(int i=1;i<=90;i++)Frame(i/30d);
        var normal=r.Pixels();SpectralBloomRenderChecks.Save(normal,Path.Combine(output,"living-fire.png"));
        foreach(var prefs in new[]{settings with{Scale=1.5f},settings with{OffsetX=.25f},settings with{OffsetY=.25f},settings with{Glow=3},new VisualizerPreferences(ColorTheme:2).ToSettings()}) {
            Frame(3,prefs);if(normal.SequenceEqual(r.Pixels()))throw new Exception("Living Fire control has no visible effect");
        }
        Frame(3,settings with{Intensity=0});if(r.Pixels().Where((_,i)=>i%4!=3).Any(v=>v!=0))throw new Exception("Living Fire intensity zero is not black");
        Frame(3);if(!normal.SequenceEqual(r.Pixels()))throw new Exception("Settings advanced the fire simulation");
        Frame(3,settings with {Background=new("solid","#204080")});
        var withBackground=r.Pixels();
        if(normal.SequenceEqual(withBackground))throw new Exception("Solid background had no effect");
        SpectralBloomRenderChecks.Save(withBackground,Path.Combine(output,"living-fire-solid-background.png"));
        Frame(3,settings with {Intensity=0,Background=new("solid","#204080")});
        var solid=r.Pixels();
        for(int i=0;i<solid.Length;i+=4)
            if(Math.Abs(solid[i]-128)>1||Math.Abs(solid[i+1]-64)>1||Math.Abs(solid[i+2]-32)>1)throw new Exception("Zero intensity obscured the background");
        var imagePath=Path.Combine(output,"background-test.png");
        var imagePixels=new byte[8*4];
        for(int i=0;i<8;i++){imagePixels[i*4+(i%4<2?2:0)]=255;imagePixels[i*4+3]=255;}
        SpectralBloomRenderChecks.Save(imagePixels,imagePath,4,2);
        var imported=BackgroundImageLibrary.Import(imagePath,Path.Combine(output,"background-library"));
        var imageSettings=settings with {Intensity=0,Background=new("image",ImagePath:imported)};
        Frame(3,imageSettings);var image=r.Pixels();
        if(image[(90*640+80)*4+2]<250||image[(90*640+560)*4]<250)throw new Exception("Image background was not composed with cover layout");
        Frame(3,imageSettings with {Scale=2,OffsetX=.5f,OffsetY=.5f});
        if(!image.SequenceEqual(r.Pixels()))throw new Exception("Moving the fire moved its background");
        Frame(3,imageSettings with {Intensity=3});SpectralBloomRenderChecks.Save(r.Pixels(),Path.Combine(output,"living-fire-image-background.png"));
        Frame(3,imageSettings with {Background=new("image",ImagePath:Path.Combine(output,"missing.png"))});
        if(r.Pixels().Where((_,i)=>i%4!=3).Any(v=>v!=0))throw new Exception("Missing image did not fall back to original");
        Frame(3,settings with {Sparks=false});if(normal.SequenceEqual(r.Pixels()))throw new Exception("Sparks toggle had no visible effect");
        Frame(3);if(!normal.SequenceEqual(r.Pixels()))throw new Exception("Background or spark edits advanced simulation");
        Console.WriteLine("PASS: solid/image background, missing-file fallback, independent cover layout and sparks toggle");
        // Two equal viewports share a device, but never share simulation state or frozen pixels.
        void Pair(double t,bool frozen) {
            r.BeginFrame();r.RenderViewport(0,0,320,360,t,silent,settings);
            r.RenderViewport(320,0,320,360,t,silent,settings,frozen);
        }
        Pair(0,false);for(int i=1;i<=60;i++)Pair(i/30d,false);
        var before=r.Pixels();Pair(2,true);for(int i=61;i<=90;i++)Pair(i/30d,true);
        var after=r.Pixels();int activeChanges=0;
        for(int y=0;y<360;y++)for(int x=0;x<640;x++)for(int c=0;c<3;c++) {
            int i=(y*640+x)*4+c;
            if(x>=320&&before[i]!=after[i])throw new Exception("Paused fire monitor changed pixels");
            if(x<320&&before[i]!=after[i])activeChanges++;
        }
        if(activeChanges<50)throw new Exception("Active fire monitor froze with its neighbor");
        double frozenTime=r.GetSimulation(320,0,320,360).Time;
        Pair(100,false);if(r.GetSimulation(320,0,320,360).Time!=frozenTime)throw new Exception("Fire resumed with catch-up");
        Pair(100+1d/30,false);if(r.GetSimulation(320,0,320,360).Time<=frozenTime)throw new Exception("Fire did not resume");
        SpectralBloomRenderChecks.Save(after,Path.Combine(output,"living-fire-monitor-freeze.png"));
        var spatialResponse=CheckFrequencySeparation(r,output);
        CheckAudioResponsiveness(r);
        CheckAudioLift(r,output);
        var gpu=Benchmark(r,()=>Frame(4),i=>Frame(4+(i+1)/30d));
        r.Dispose();
        var coverage=new List<object>();
        foreach(var (width,height) in new[]{(640,360),(1280,360),(360,640)})
        {
            using var monitor=new FireGpuRenderer(hwnd,width,height);
            for(int i=0;i<=60;i++) {
                monitor.BeginFrame();monitor.RenderViewport(0,0,width,height,i/30d,silent,settings);
            }
            var pixels=monitor.Pixels();
            // Every screen column must contain visible fire near the base,
            // including the outermost columns that previously remained empty.
            for(int x=0;x<width;x++) {
                int brightest=0;
                for(int y=height*3/4;y<height;y++)brightest=Math.Max(brightest,pixels[(y*width+x)*4+2]);
                if(brightest<20)throw new Exception($"Uncovered fire column {x} at {width}x{height}: {brightest}");
            }
            int flames=monitor.GetSimulation(0,0,width,height).FlameCount;
            coverage.Add(new{width,height,flames,everyColumnCovered=true});
            SpectralBloomRenderChecks.Save(pixels,Path.Combine(output,$"living-fire-coverage-{width}x{height}.png"),width,height);
        }
        using var desktop=new FireGpuRenderer(hwnd,3840,2160);
        void Desktop(double time) {desktop.BeginFrame();desktop.RenderViewport(0,0,3840,2160,time,silent,settings);}
        Desktop(0);for(int i=1;i<=30;i++)Desktop(i/30d);
        var gpu4K=Benchmark(desktop,()=>Desktop(1),i=>Desktop(1+(i+1)/30d));
        File.WriteAllText(Path.Combine(output,"living-fire-checks.json"),JsonSerializer.Serialize(new{adapter=desktop.AdapterName,speedMultiplier=FireSimulation.SpeedMultiplier,coverage,spatialResponse,controls=true,zeroIntensity=true,independentMonitorPause=true,resumeWithoutCatchup=true,gpuMilliseconds=gpu,gpu4KMilliseconds=gpu4K,simulationGrid=new[]{FireSimulation.X,FireSimulation.Y,FireSimulation.Z}},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS: Living Fire controls, zero intensity, two monitor states, resume; GPU median {gpu.Order().ElementAt(gpu.Length/2):F2} ms at 640x360, {gpu4K.Order().ElementAt(gpu4K.Length/2):F2} ms at 4K (30 FPS)");
    }
    // The flame must read as reacting to beats, not lagging behind them. Around 120 ms of playback
    // (7 logical ticks = 28 physics substeps) should raise the envelope to most of the target and,
    // once silent, let it fall well back. Thresholds are loose enough to allow tuning but tight
    // enough to catch a regression to the old slow attack (~0.75 after 120 ms) or release.
    static void CheckAudioResponsiveness(FireGpuRenderer renderer)
    {
        var sim=renderer.GetSimulation(0,0,640,360);
        sim.Reset();
        sim.AudioTarget=new System.Numerics.Vector3(1,1,1);
        for(int i=0;i<7;i++)sim.Step();
        float attack=sim.AudioEnvelope.X;
        if(attack<.85f)throw new Exception($"Audio attack too slow to read as reactive: {attack:F2} after ~120 ms");
        sim.AudioTarget=new System.Numerics.Vector3(0,0,0);
        for(int i=0;i<7;i++)sim.Step();
        float release=sim.AudioEnvelope.X;
        if(release>.6f)throw new Exception($"Audio release lingers, blurring beats: {release:F2} after ~120 ms of silence");
        sim.Reset();
        Console.WriteLine($"PASS: audio envelope reacts quickly (attack {attack:F2}, release {release:F2} after ~120 ms)");
    }
    // Audio must be visible, not just measurable: a moderate music-like level (energy ~0.47 after
    // gain) has to raise clearly more fire into the upper part of the screen than silence, so the
    // flame reads as reacting to the beat rather than merely warming a little.
    static void CheckAudioLift(FireGpuRenderer renderer,string output)
    {
        byte[] Sequence(Func<int,float> band)
        {
            var sim=renderer.GetSimulation(0,0,640,360);sim.Reset();
            var spectrum=Enumerable.Range(0,64).Select(band).ToArray();
            var profile=new AethelisAudioProfile(0,0,0,0,0);
            var prefs=new VisualizerPreferences(Glow:0,ColorTheme:1).ToSettings();
            for(int i=0;i<90;i++){renderer.BeginFrame();renderer.RenderViewport(0,0,640,360,i/30d,profile,prefs,spectrum:spectrum);}
            renderer.RenderPreview(false);return renderer.Pixels();
        }
        // Count lit fire pixels in the upper 45% of the screen within [x0,x1); silence barely reaches it.
        int Upper(byte[] px,int x0,int x1){int n=0;for(int y=0;y<162;y++)for(int x=x0;x<x1;x++)if(px[(y*640+x)*4+2]>40)n++;return n;}
        // A bass-heavy spectrum (like a kick) must lift a tall column; silence stays low.
        var quiet=Sequence(_=>0);var bass=Sequence(i=>i<20?.45f:.03f);
        SpectralBloomRenderChecks.Save(bass,Path.Combine(output,"living-fire-audio-lift.png"));
        int q=Upper(quiet,0,640),l=Upper(bass,0,640);
        if(l<1500||l<q*3)throw new Exception($"Audio does not visibly raise the flame: quiet={q}, loud={l}");
        // Separation must be visible, not a uniform wall: bass (routed right) lifts the right far more
        // than the left. This guards the spectral-contrast emphasis added to FireFrequencyBands.
        int right=Upper(bass,384,640),left=Upper(bass,0,256);
        if(right<800||right<left*3)throw new Exception($"Bands do not separate visibly: left={left}, right={right}");
        renderer.GetSimulation(0,0,640,360).Reset();
        Console.WriteLine($"PASS: audio lifts the flame (upper {l}) and bands separate (right {right} >> left {left})");
    }
    static Dictionary<string,double[]> CheckFrequencySeparation(FireGpuRenderer renderer,string output)
    {
        var sim=renderer.GetSimulation(0,0,640,360);
        sim.Reset();sim.SetFrequencyTargets(new float[sim.FlameCount]);for(int i=0;i<60;i++)sim.Step();
        if(Math.Abs(sim.Time-4)>1e-9)throw new Exception("Living Fire is not exactly four times as fast");
        double[] Scenario(int? group,string name)
        {
            sim.Reset();sim.SetFrequencyTargets(new float[sim.FlameCount]);for(int i=0;i<120;i++)sim.Step();
            var bands=new float[64];
            if(group is int g)for(int bin=g*64/9;bin<(g+1)*64/9;bin++)bands[bin]=1;
            // Send the raw spectrum through the product renderer, keeping the grouped
            // profile silent, so this also catches accidentally dropping the FFT input.
            renderer.BeginFrame();renderer.RenderViewport(0,0,640,360,3,new AethelisAudioProfile(0,0,0,0,0),
                new VisualizerPreferences(Glow:0,ColorTheme:1).ToSettings(),spectrum:bands);
            for(int i=0;i<60;i++)sim.Step();
            renderer.RenderPreview(false);SpectralBloomRenderChecks.Save(renderer.Pixels(),Path.Combine(output,$"living-fire-{name}.png"));
            return sim.HeatByZone();
        }
        var quiet=Scenario(null,"frequency-quiet");var results=new Dictionary<string,double[]>();
        foreach(var (group,name,third) in new[]{(8,"treble-left",0),(4,"mids-center",1),(0,"bass-right",2)})
        {
            var zones=Scenario(group,name);var increase=new double[3];
            for(int i=0;i<9;i++)increase[i/3]+=Math.Max(0,zones[i]-quiet[i]);
            double elsewhere=increase.Where((_,i)=>i!=third).Max();
            if(increase[third]<100||increase[third]<elsewhere*3)
                throw new Exception($"Frequency is not localized: {name} => {string.Join(", ",increase)}");
            results[name]=increase;
        }
        Console.WriteLine("PASS: 4x simulation speed; isolated FFT treble/center/bass drive left/center/right fire regions");
        return results;
    }
    [StructLayout(LayoutKind.Sequential)] struct ClockData {public ulong Frequency;public int Disjoint;}
    static double[] Benchmark(FireGpuRenderer renderer,Action warmup,Action<int> frame)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var device=(ID3D11Device)typeof(FireGpuRenderer).GetField("device",flags)!.GetValue(renderer)!;
        var context=(ID3D11DeviceContext)typeof(FireGpuRenderer).GetField("context",flags)!.GetValue(renderer)!;
        using var clock=device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
        using var begin=device.CreateQuery(new QueryDescription(QueryType.Timestamp));using var end=device.CreateQuery(new QueryDescription(QueryType.Timestamp));
        var samples=new List<double>();warmup();
        for(int i=0;i<12;i++) {
            context.Begin(clock);context.End(begin);frame(i);context.End(end);context.End(clock);context.Flush();
            var timing=Read<ClockData>(context,clock);ulong first=Read<ulong>(context,begin),last=Read<ulong>(context,end);
            if(timing.Disjoint==0&&timing.Frequency>0)samples.Add((last-first)*1000d/timing.Frequency);
        }
        if(samples.Count==0)throw new Exception("GPU timestamp samples were disjoint");return samples.ToArray();
    }
    static T Read<T>(ID3D11DeviceContext context,ID3D11Query query) where T:struct {
        var memory=Marshal.AllocHGlobal(Marshal.SizeOf<T>());var watch=Stopwatch.StartNew();
        try {while(true) {
            var result=context.GetData(query,memory,(uint)Marshal.SizeOf<T>(),AsyncGetDataFlags.None);
            if(result.Code==0)return Marshal.PtrToStructure<T>(memory);
            result.CheckError();if(watch.Elapsed.TotalSeconds>5)throw new TimeoutException("GPU timing readback timed out");Thread.Sleep(1);
        }}finally{Marshal.FreeHGlobal(memory);}
    }
}
