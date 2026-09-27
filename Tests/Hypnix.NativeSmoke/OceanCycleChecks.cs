using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AnimatedWallPaper.Services;

internal static class OceanCycleChecks
{
    private static byte[] SurfaceHash(OceanGpuRenderer renderer)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var band in renderer.Spectrum!.Bands)
        foreach(var field in OceanSpectrumChecks.ReadFields(renderer.Device,renderer.Context,band))
            hash.AppendData(MemoryMarshal.AsBytes(field.AsSpan()));
        return hash.GetHashAndReset();
    }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,1280,720);
        var config=OceanCelestialSettings.Default;
        var settings=new OceanSettings(Celestial:config);
        var results=new List<object>();
        renderer.Render(10,new OceanSettings());
        var waveHash=SurfaceHash(renderer);
        foreach(var (name,utc) in new[]
        {
            ("sun-first-contact",new DateTimeOffset(2026,9,27,6,29,0,TimeSpan.Zero)),
            ("sun-1deg",new DateTimeOffset(2026,9,27,6,38,0,TimeSpan.Zero)),
            ("sun-5deg",new DateTimeOffset(2026,9,27,6,59,0,TimeSpan.Zero)),
            ("sun-10deg",new DateTimeOffset(2026,9,27,7,29,0,TimeSpan.Zero)),
            ("noon",new DateTimeOffset(2026,9,27,12,30,0,TimeSpan.Zero)),
            ("sunset",new DateTimeOffset(2026,9,27,18,15,0,TimeSpan.Zero)),
            ("moon-first-contact",new DateTimeOffset(2026,9,27,18,38,0,TimeSpan.Zero)),
            ("moon-1deg",new DateTimeOffset(2026,9,27,18,48,0,TimeSpan.Zero)),
            ("moon-5deg",new DateTimeOffset(2026,9,27,19,8,0,TimeSpan.Zero)),
            ("moon-10deg",new DateTimeOffset(2026,9,27,19,38,0,TimeSpan.Zero)),
            ("moon-set",new DateTimeOffset(2026,9,28,7,45,0,TimeSpan.Zero)),
            ("crescent",new DateTimeOffset(2026,9,16,19,20,0,TimeSpan.Zero))
        })
        {
            renderer.Render(10,settings,celestialUtc:utc);
            var pixels=renderer.Pixels();
            OceanRenderChecks.Save(pixels,Path.Combine(output,name+".png"),renderer.Width,renderer.Height);
            if(!waveHash.SequenceEqual(SurfaceHash(renderer))) throw new Exception("Celestial lighting altered approved spectral fields.");
            var count=renderer.CycleSky!.BuildCount;
            renderer.Render(10,settings,celestialUtc:utc);
            if(!pixels.SequenceEqual(renderer.Pixels()) || renderer.CycleSky.BuildCount!=count) throw new Exception("Paused sky changes or rebuilds.");
            var frame=renderer.CelestialFrame!;
            results.Add(new { name,utc,sunElevation=frame.Sun.AirlessElevation,moonElevation=frame.Moon.AirlessElevation,
                frame.PhaseAngle,frame.Exposure,frame.MoonPrime,frame.MoonEast,frame.MoonNorth,
                meanPixel=pixels.Where((_,i)=>i%4!=3).Average(x=>(double)x) });
        }
        var instant=config.EpochUtc.AddMinutes(12);
        renderer.Render(10,settings,celestialUtc:instant); var first=renderer.Pixels();
        renderer.Render(11,settings,celestialUtc:instant.AddSeconds(24));
        if(first.SequenceEqual(renderer.Pixels())) throw new Exception("Cycle did not animate.");
        renderer.Render(10,settings,celestialUtc:instant);
        if(!first.SequenceEqual(renderer.Pixels())) throw new Exception("Seek is not deterministic.");
        using(var recreated=new OceanGpuRenderer(IntPtr.Zero,1280,720))
        {
            recreated.Render(10,settings,celestialUtc:instant);
            if(!first.SequenceEqual(recreated.Pixels())) throw new Exception("GPU recreation changes celestial frame.");
        }
        foreach(var quality in Enum.GetValues<OceanQuality>())
        {
            renderer.Render(10,settings with {Quality=quality},celestialUtc:new DateTimeOffset(2026,9,27,19,8,0,TimeSpan.Zero));
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,$"moon-quality-{quality}.png"),1280,720);
        }
        using(var high=new OceanGpuRenderer(IntPtr.Zero,3840,2160))
        {
            var utc=new DateTimeOffset(2026,9,27,19,8,0,TimeSpan.Zero);
            high.Render(10,settings with {Quality=OceanQuality.High},celestialUtc:utc);
            OceanRenderChecks.Save(high.Pixels(),Path.Combine(output,"moon-4k.png"),3840,2160);
        }
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new
        {
            renderer.AdapterName,scenes=results,pauseDeterministic=true,seekDeterministic=true,
            preservedWaveFields=true,recreationDeterministic=true,qualityMatrix=true,native4k=true
        },new JsonSerializerOptions {WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("PASS: celestial scenes, spectral preservation, pause, seek, GPU recreation, quality levels and 4K.");
    }
}
