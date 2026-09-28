using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal static class OceanCloudMotionChecks
{
    private const int Size=128;
    private static float[] Sample(OceanGpuRenderer renderer,OceanSettings settings,OceanCloudTrajectory trajectory,
        double time,Vector2 origin,ID3D11ComputeShader shader,ID3D11Buffer constants,ID3D11Texture2D output,
        ID3D11UnorderedAccessView write,ID3D11Texture2D staging)
    {
        renderer.Volumes!.BindDensityDiagnostic(time,settings,trajectory);
        var map=renderer.Context.Map(constants,0,MapMode.WriteDiscard);
        Marshal.StructureToPtr(new Vector4(origin.X,origin.Y,96,Size),map.DataPointer,false);
        renderer.Context.Unmap(constants,0);
        renderer.Context.CSSetConstantBuffer(2,constants); renderer.Context.CSSetShader(shader);
        renderer.Context.CSSetUnorderedAccessView(0,write); renderer.Context.Dispatch(Size/8,Size/8,1);
        renderer.Context.CSSetUnorderedAccessView(0,null!); renderer.Context.CSSetShader(null!);
        renderer.Context.CSSetShaderResource(23,null!);
        renderer.Context.CopyResource(staging,output);
        var values=new float[Size*Size*4]; map=renderer.Context.Map(staging,0,MapMode.Read);
        try { for(var y=0;y<Size;y++) Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,values,y*Size*4,Size*4); }
        finally { renderer.Context.Unmap(staging,0); }
        for(var i=0;i<values.Length;i+=4)
            if(!float.IsFinite(values[i]+values[i+1]+values[i+2]) || values[i]<0 || values[i+2]>5 || values[i+1]<0)
                throw new Exception("Cloud/fog density is non-finite or outside bounds.");
        return values;
    }
    private static double Difference(float[] a,float[] b,int channel=0)
    { double sum=0; for(var i=channel;i<a.Length;i+=4) sum+=Math.Abs(a[i]-b[i]); return sum/(a.Length/4); }
    private static double Occupancy(float[] data)
    { var count=0; for(var i=0;i<data.Length;i+=4) if(data[i]>.05) count++; return count/(double)(data.Length/4); }
    internal static void Movie(string folder,string encoder)
    {
        Directory.CreateDirectory(folder);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,960,540);
        var temporalResults=new List<object>();
        foreach(var kind in Enum.GetValues<OceanCloudType>())
        {
            var start=new ProcessStartInfo(encoder) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
            foreach(var arg in new[] {"-hide_banner","-loglevel","error","-y","-f","rawvideo","-pixel_format","bgra",
                "-video_size","960x540","-framerate","30","-i","pipe:0","-an","-c:v","libx264","-preset","fast","-crf","0",
                "-pix_fmt","yuv420p","-movflags","+faststart",Path.Combine(folder,$"{kind}-120s.mp4")}) start.ArgumentList.Add(arg);
            using var process=Process.Start(start) ?? throw new Exception("Cannot start the supplied encoder.");
            var errors=process.StandardError.ReadToEndAsync();
            var celestial=OceanCelestialSettings.Default with {EpochUtc=new(2026,9,27,7,10,0,TimeSpan.Zero),TimeScale=1};
            var settings=new OceanSettings(Celestial:celestial,Weather:new(Clouds:kind,Coverage:.48f));
            var trajectory=new OceanCloudTrajectory(9);
            byte[]? previous=null; var differences=new List<double>(); var peak=0d; var peakFrame=0;
            try
            {
                for(var i=0;i<3600;i++)
                {
                    var time=i/30d;
                    renderer.Render(10,settings,celestialUtc:celestial.EpochUtc.AddSeconds(time),weatherTime:time,cloudTrajectory:trajectory);
                    var pixels=renderer.Pixels(); process.StandardInput.BaseStream.Write(pixels);
                    if(previous is not null)
                    {
                        // Fixed spatial sample before video encoding, so periodic
                        // encoder keyframe quantization cannot look like a cloud jump.
                        double delta=0; var count=0;
                        for(var p=0;p<pixels.Length;p+=64)
                        {
                            delta+=(Math.Abs(pixels[p]-previous[p])+Math.Abs(pixels[p+1]-previous[p+1])+Math.Abs(pixels[p+2]-previous[p+2]))/3d;
                            count++;
                        }
                        delta/=count; differences.Add(delta);
                        if(delta>peak) { peak=delta; peakFrame=i; }
                    }
                    previous=pixels;
                    if(i%900==0 || i==3599) OceanRenderChecks.Save(pixels,Path.Combine(folder,$"{kind}-{i:0000}.png"),960,540);
                    if(i%1800==0) Console.WriteLine($"Video {kind}: {i/30}s / 120s.");
                }
                process.StandardInput.Close(); process.WaitForExit();
                var error=errors.GetAwaiter().GetResult(); if(process.ExitCode!=0) throw new Exception(error);
                differences.Sort();
                temporalResults.Add(new {kind=kind.ToString(),frames=3600,meanCodeDifference=differences.Average(),
                    p95CodeDifference=differences[(int)(differences.Count*.95)],maxCodeDifference=peak,peakFrame});
                File.WriteAllText(Path.Combine(folder,"raw-temporal-checks.json"),JsonSerializer.Serialize(temporalResults,new JsonSerializerOptions {WriteIndented=true}));
                if(peak>.5) throw new Exception($"Temporal cloud jump: {kind} frame {peakFrame}, mean RGB difference {peak} / 255.");
            }
            finally { if(!process.HasExited) process.Kill(); }
            Console.WriteLine($"Saved {kind}: 120 seconds, 30 FPS, weather/celestial time 1x, fixed water.");
        }
    }
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,1280,720);
        var utc=new DateTimeOffset(2026,9,27,8,0,0,TimeSpan.Zero);
        var settings=new OceanSettings(Celestial:OceanCelestialSettings.Default,Weather:new(Fog:OceanFog.Banks));
        renderer.Render(10,settings,celestialUtc:utc,weatherTime:0);
        using var shader=renderer.Device.CreateComputeShader(OceanShader.Compile(Path.Combine(AppContext.BaseDirectory,"Shaders","OceanVolume.hlsl"),"CloudDensityReference","cs_5_0").Span);
        using var constants=renderer.Device.CreateBuffer(16,BindFlags.ConstantBuffer,ResourceUsage.Dynamic,CpuAccessFlags.Write);
        var desc=new Texture2DDescription(Format.R32G32B32A32_Float,Size,Size,1,1,BindFlags.UnorderedAccess);
        using var output=renderer.Device.CreateTexture2D(desc); using var write=renderer.Device.CreateUnorderedAccessView(output);
        desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
        using var staging=renderer.Device.CreateTexture2D(desc);
        float[] Read(OceanSettings s,OceanCloudTrajectory path,double t,bool compensate=false)
        {
            var travel=path.Constants(t,s.Weather!.Clouds).Travel;
            var origin=compensate ? -new Vector2(travel.X,travel.Y) : Vector2.Zero;
            return Sample(renderer,s,path,t,origin,shader,constants,output,write,staging);
        }
        var results=new List<object>();
        foreach(var kind in Enum.GetValues<OceanCloudType>())
        foreach(var coverage in new[] {0f,.25f,.48f,.75f,.95f})
        foreach(var wind in new[] {0f,9f,18f})
        {
            var s=settings with {Weather=settings.Weather! with {Clouds=kind,Coverage=coverage,WindMetresPerSecond=wind}};
            var path=new OceanCloudTrajectory(wind);
            var first=Read(s,path,0); var later=Read(s,path,120,true);
            var fixedArea=Read(s,path,120); var calmAtSameTime=Read(s,new OceanCloudTrajectory(0),120);
            if(Difference(fixedArea,calmAtSameTime,1)!=0) throw new Exception("Cloud motion changed the fog field.");
            if(!later.SequenceEqual(Read(s,path,120,true))) throw new Exception("Cloud density is not deterministic.");
            var change=Difference(first,later); var occupancy=Occupancy(first); var laterOccupancy=Occupancy(later);
            if(coverage==0 && (occupancy!=0 || laterOccupancy!=0 || change!=0)) throw new Exception("Zero coverage emitted cloud density.");
            if(coverage>=.48f && change<1e-5) throw new Exception($"{kind} did not evolve in material space.");
            if(Math.Abs(occupancy-laterOccupancy)>.03) throw new Exception($"Cloud occupancy drifted for {kind}/{coverage}/{wind}: {occupancy} -> {laterOccupancy}.");
            results.Add(new {kind=kind.ToString(),coverage,wind,change,occupancy,laterOccupancy});
        }
        var motion=new OceanCloudMotion(9); var before=motion.Trajectory;
        motion.SetWind(10.25,18); var after=motion.Trajectory;
        foreach(var time in new[] {10d,10.25,10.5,11})
            if(!Read(settings,before,time).SequenceEqual(Read(settings,after,time))) throw new Exception("Wind edit changed an already displayed density endpoint.");
        // At the integrated-distance renewal boundary the zero-weight field resets
        // continuously. Spatial phases make most samples cross at other instants.
        var renewal=new OceanCloudTrajectory(18);
        foreach(var t in new[] {3/.018,3600d,86400d,604800d,1600/(.018*3/Math.Sqrt(10))})
        {
            var a=Read(settings,renewal,t-.001); var b=Read(settings,renewal,t+.001);
            if(Difference(a,b)>.002) throw new Exception($"Cloud density jumped at long-time/period boundary {t}.");
        }
        var longCoverage=new List<object>();
        foreach(var kind in Enum.GetValues<OceanCloudType>())
        foreach(var seed in new[] {0,27,101})
        foreach(var coverage in new[] {.25f,.75f})
        {
            var s=settings with {Weather=new(Clouds:kind,Seed:seed,Coverage:coverage,WindMetresPerSecond:18)};
            var samples=new List<double>();
            for(var t=0;t<=600;t+=60) samples.Add(Occupancy(Read(s,renewal,t,true)));
            var drift=samples.Max()-samples.Min();
            if(drift>.03) throw new Exception($"Ten-minute coverage drift: {kind}/{seed}/{coverage}, {drift}.");
            longCoverage.Add(new {kind=kind.ToString(),seed,coverage,minimum=samples.Min(),maximum=samples.Max(),drift});
        }
        var clear=settings with {Weather=new(Coverage:.48f,Fog:OceanFog.Clear)};
        renderer.Render(10,clear,celestialUtc:utc,weatherTime:10.25,cloudTrajectory:before);
        var oldPixels=renderer.Pixels();
        clear=clear with {Weather=clear.Weather! with {WindMetresPerSecond=18}};
        renderer.Render(10,clear,celestialUtc:utc,weatherTime:10.25,cloudTrajectory:after);
        if(!oldPixels.SequenceEqual(renderer.Pixels())) throw new Exception("Replacing the future cloud cache jumped the displayed image.");
        var epoch=utc.AddSeconds(-10.25*clear.Celestial!.TimeScale);
        for(var step=1;step<=28;step++)
        {
            var t=10.25+step*.1;
            renderer.Render(10,clear,celestialUtc:epoch.AddSeconds(t*clear.Celestial.TimeScale),weatherTime:t,cloudTrajectory:after);
        }
        var stagedPixels=renderer.Pixels();
        using(var recreated=new OceanGpuRenderer(IntPtr.Zero,1280,720))
        {
            recreated.Render(10,clear,celestialUtc:epoch.AddSeconds(13.05*clear.Celestial.TimeScale),weatherTime:13.05,cloudTrajectory:after);
            if(!stagedPixels.SequenceEqual(recreated.Pixels())) throw new Exception("Cloud trajectory changed after incremental cache/recreation.");
        }
        // Integrated visual snapshots keep the water fixed, and the same source
        // position, so animation cannot be confused with changing illumination.
        foreach(var kind in Enum.GetValues<OceanCloudType>())
        foreach(var time in new[] {0d,30,120})
        {
            var s=settings with {Weather=new(Clouds:kind,Coverage:.48f)};
            renderer.Render(10,s,celestialUtc:utc,weatherTime:time,cloudTrajectory:new(9));
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(folder,$"{kind}-{time:000}.png"),1280,720);
        }
        File.WriteAllText(Path.Combine(folder,"cloud-motion.json"),JsonSerializer.Serialize(new {
            renderer.AdapterName,isolatedFog=true,deterministic=true,continuousWind=true,continuousDisplayedImage=true,
            trajectoryRecreation=true,stagedTrajectoryCache=true,longDuration=true,scenes=results,longCoverage},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("PASS: 45 cloud control combinations; material evolution, coverage stability, fog isolation, wind continuity and long duration.");
    }
}
