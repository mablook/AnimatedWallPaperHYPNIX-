using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class OceanReflectionChecks
{
    private sealed record Measurement(double Peak,int PeakX,int PeakY,double Total,double X,double Y,double BrightX,double BrightY);
    private static Measurement Measure(OceanGpuRenderer renderer)
    {
        var description=renderer.HdrScene.Description;
        description.BindFlags=BindFlags.None; description.Usage=ResourceUsage.Staging;
        description.CPUAccessFlags=CpuAccessFlags.Read; description.MiscFlags=ResourceOptionFlags.None;
        using var staging=renderer.Device.CreateTexture2D(description);
        renderer.Context.CopyResource(staging,renderer.HdrScene);
        var map=renderer.Context.Map(staging,0,MapMode.Read);
        var luminance=new double[description.Width*description.Height];
        try
        {
            var row=new byte[description.Width*8];
            for(var y=0;y<description.Height;y++)
            {
                Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,row,0,row.Length);
                for(var x=0;x<description.Width;x++)
                {
                    var r=(double)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row,x*8));
                    var g=(double)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row,x*8+2));
                    var b=(double)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row,x*8+4));
                    if(!double.IsFinite(r+g+b) || Math.Min(r,Math.Min(g,b))<0) throw new Exception("Invalid reflection HDR.");
                    luminance[y*description.Width+x]=r*.2126+g*.7152+b*.0722;
                }
            }
        }
        finally { renderer.Context.Unmap(staging,0); }
        var peak=luminance.Max(); var peakIndex=Array.IndexOf(luminance,peak);
        double total=0,xSum=0,ySum=0,thresholdTotal=0,thresholdX=0,thresholdY=0;
        for(var i=0;i<luminance.Length;i++)
        {
            var light=luminance[i]; var x=i%(int)description.Width; var y=i/(int)description.Width;
            total+=light; xSum+=x*light; ySum+=y*light;
            if(light>peak*.2) {thresholdTotal+=light;thresholdX+=x*light;thresholdY+=y*light;}
        }
        return new(peak,peakIndex%(int)description.Width,peakIndex/(int)description.Width,
            total,xSum/Math.Max(total,1e-30),ySum/Math.Max(total,1e-30),
            thresholdX/Math.Max(thresholdTotal,1e-30),thresholdY/Math.Max(thresholdTotal,1e-30));
    }
    private static Vector2 MirrorPixel(OceanCelestialBody body,int width,int height)
    {
        // Independent planar mirror oracle: reflect the source direction through Y=0.
        var elevation=body.ApparentElevation;
        var azimuth=Math.Atan2(body.Direction.X,body.Direction.Z);
        var ray=new Vector3((float)(Math.Sin(azimuth)*Math.Cos(elevation)),
            (float)-Math.Sin(elevation),(float)(Math.Cos(azimuth)*Math.Cos(elevation)));
        var forward=new Vector3(0,-MathF.Sin(.16f),MathF.Cos(.16f));
        var up=new Vector3(0,MathF.Cos(.16f),MathF.Sin(.16f));
        var z=Vector3.Dot(ray,forward);
        if(z<=0) return new(float.PositiveInfinity,float.PositiveInfinity);
        return new((ray.X/(z*.383864f*width/height)+1)*width*.5f-.5f,
            (1-Vector3.Dot(ray,up)/(z*.383864f))*height*.5f-.5f);
    }
    private static void CheckReflectionLut(OceanGpuRenderer renderer)
    {
        var texture=renderer.ReflectionLut!.Texture;
        var description=texture.Description;
        description.BindFlags=BindFlags.None; description.Usage=ResourceUsage.Staging;
        description.CPUAccessFlags=CpuAccessFlags.Read;
        using var staging=renderer.Device.CreateTexture2D(description);
        renderer.Context.CopyResource(staging,texture);
        var map=renderer.Context.Map(staging,0,MapMode.Read);
        var data=new byte[128*128*4];
        try
        {
            for(var y=0;y<128;y++) Marshal.Copy(map.DataPointer+y*(int)map.RowPitch,data,y*128*4,128*4);
        }
        finally { renderer.Context.Unmap(staging,0); }
        double Read(int x,int y,int channel)=>(double)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data,(y*128+x)*4+channel*2));
        for(var y=0;y<128;y++) for(var x=0;x<128;x++)
        {
            var a=Read(x,y,0); var b=Read(x,y,1);
            if(!double.IsFinite(a+b) || b<0 || b>a || a>1) throw new Exception("Reflection LUT violates energy bounds.");
        }
        // Independent double-precision 262144-sample integration, not the 1024 GPU samples.
        foreach(var (x,y,a,b) in new[]
        {
            (0,0,1d,1d),(16,0,1d,.92311939),(127,0,1d,0d),
            (0,33,.96230645,.32035985),(16,33,.96109912,.30585447),
            (40,33,.92767606,.22460160),(90,33,.94250838,.03085713),
            (127,33,.99998546,.00000161),(16,127,.92318834,.04714361),(127,127,.46156618,.00007338)
        })
            if(Math.Abs(Read(x,y,0)-a)>.0015 || Math.Abs(Read(x,y,1)-b)>.0015)
                throw new Exception($"Reflection LUT does not match independent integral at {x}/{y}.");
    }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,1280,720);
        var settings=new OceanSettings(Bloom:false,Celestial:OceanCelestialSettings.Default with {Air=OceanAir.Clear},
            Weather:new OceanWeatherSettings(Fog:OceanFog.Clear,Coverage:0));
        var results=new List<object>();
        foreach(var composition in Enum.GetValues<OceanSkyComposition>())
        foreach(var moon in new[] {false,true})
        foreach(var hour in new[] {0d,.5,1.5,3,5})
        {
            settings=settings with {Celestial=settings.Celestial! with {Composition=composition}};
            var utc=new DateTimeOffset(2026,9,27,moon ? 18 : 6,moon ? 48 : 38,0,TimeSpan.Zero).AddHours(hour);
            renderer.Diagnostic=OceanRenderDiagnostic.None; renderer.FlatDiagnosticSurface=false;
            renderer.Render(10,settings,celestialUtc:utc,weatherTime:10);
            var body=moon ? renderer.CelestialFrame!.Moon : renderer.CelestialFrame!.Sun;
            var name=$"{composition}-{(moon ? "moon" : "sun")}-{hour:F1}";
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,name+"-full.png"),1280,720);
            foreach(var flat in new[] {false,true})
            {
                renderer.Diagnostic=moon ? OceanRenderDiagnostic.MoonDirect : OceanRenderDiagnostic.SunDirect;
                renderer.FlatDiagnosticSurface=flat;
                renderer.Render(10,settings,celestialUtc:utc,weatherTime:10);
                OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,name+$"-direct-flat{flat}.png"),1280,720);
                var measured=Measure(renderer); var expected=MirrorPixel(body,1280,720);
                var inFrame=expected.X>8 && expected.X<1272 && expected.Y>225 && expected.Y<712;
                // Allow the finite disk, residual .005 slope width and lunar albedo
                // to shift the brightest pixel within the sub-degree mirror lobe.
                if(flat && inFrame && (Math.Abs(measured.PeakX-expected.X)>16 || Math.Abs(measured.PeakY-expected.Y)>16))
                    throw new Exception($"Reflection does not follow {name}: peak {measured.PeakX}/{measured.PeakY}, expected {expected}.");
                if(flat && expected.Y>820 && measured.Peak>1e-5) throw new Exception($"High {name} still emits a false in-frame mirror highlight.");
                results.Add(new {composition=composition.ToString(),moon,hour,utc,body.AirlessElevation,body.ApparentElevation,
                    body.Direction,flat,expected=double.IsFinite(expected.X) ? new {expected.X,expected.Y} : null,measurement=measured});
            }
            Console.WriteLine($"Captured {name}: altitude {body.AirlessElevation:F2} deg.");
        }
        CheckReflectionLut(renderer);
        File.WriteAllText(Path.Combine(output,"reflection.json"),JsonSerializer.Serialize(new {
            renderer.AdapterName,mirrorTracking=true,highBodyLeavesFrame=true,finiteHdr=true,
            reflectionLutIntegral=true,reflectionLutBounds=true,scenes=results},new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("PASS: solar/lunar mirror tracking, geographic/cinematic paths, high-source rejection and rough reflectance integral.");
    }
}
