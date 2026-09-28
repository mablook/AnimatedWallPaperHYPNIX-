using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal static class OceanVolumeChecks
{
    private static void CheckTransport(OceanGpuRenderer renderer)
    {
        using var shader=renderer.Device.CreateComputeShader(OceanShader.Compile(
            Path.Combine(AppContext.BaseDirectory,"Shaders","OceanVolume.hlsl"),"TransportReference","cs_5_0").Span);
        var desc=new Texture2DDescription(Format.R32G32B32A32_Float,13,2,1,1,BindFlags.UnorderedAccess);
        using var texture=renderer.Device.CreateTexture2D(desc);
        using var write=renderer.Device.CreateUnorderedAccessView(texture);
        renderer.Context.CSSetShader(shader); renderer.Context.CSSetUnorderedAccessView(0,write);
        renderer.Context.Dispatch(2,1,1); renderer.Context.CSSetUnorderedAccessView(0,null!); renderer.Context.CSSetShader(null!);
        desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
        using var staging=renderer.Device.CreateTexture2D(desc);
        renderer.Context.CopyResource(staging,texture);
        var mapped=renderer.Context.Map(staging,0,MapMode.Read);
        try
        {
            double[] sigma=[0,1e-7,.01,.1,1,10], factors=[1,2,.5], source=[.3,.8,1.2];
            for(var y=0;y<2;y++)
            {
                var row=new float[13*4]; Marshal.Copy(mapped.DataPointer+y*(int)mapped.RowPitch,row,0,row.Length);
                for(var x=0;x<13;x++) for(var c=0;c<3;c++)
                {
                    var extinction=sigma[x%6]*factors[c];
                    var expected=y==0 ? (x==12 ? 0 : source[c])*OceanVolumeMath.SegmentWeight(extinction,7.3) : OceanVolumeMath.Transmission(extinction,7.3);
                    if(!float.IsFinite(row[x*4+c]) || Math.Abs(row[x*4+c]-expected)>Math.Max(1e-6,Math.Abs(expected)*2e-5))
                        throw new Exception($"GPU transport differs from analytic reference at {x}/{y}/{c}.");
                }
            }
        }
        finally { renderer.Context.Unmap(staging,0); }
        Console.WriteLine("PASS: GPU vacuum/homogeneous transport and step subdivision vs double-precision reference.");
    }
    private static byte[] SurfaceHash(OceanGpuRenderer renderer)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var band in renderer.Spectrum!.Bands)
        foreach(var field in OceanSpectrumChecks.ReadFields(renderer.Device,renderer.Context,band))
            hash.AppendData(MemoryMarshal.AsBytes(field.AsSpan()));
        return hash.GetHashAndReset();
    }
    private static void CheckVolume(OceanGpuRenderer renderer,OceanVolumetrics.Volume volume,bool transmission)
    {
        var desc=volume.Texture.Description;
        desc.Usage=ResourceUsage.Staging; desc.BindFlags=BindFlags.None; desc.CPUAccessFlags=CpuAccessFlags.Read;
        using var staging=renderer.Device.CreateTexture3D(desc);
        renderer.Context.CopyResource(staging,volume.Texture);
        var map=renderer.Context.Map(staging,0,MapMode.Read);
        try
        {
            var row=new byte[desc.Width*8];
            for(var z=0;z<desc.Depth;z++) for(var y=0;y<desc.Height;y++)
            {
                Marshal.Copy(map.DataPointer+(int)(z*map.DepthPitch+y*map.RowPitch),row,0,row.Length);
                for(var i=0;i<row.Length;i+=8) for(var c=0;c<3;c++)
                {
                    var value=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(row,i+c*2));
                    if(!float.IsFinite(value) || value<0 || (transmission && value>1))
                        throw new Exception("Invalid volumetric radiance/transmittance.");
                    if(transmission && z==0 && value!=1) throw new Exception("Zero-distance transmission is not identity.");
                }
            }
        }
        finally { renderer.Context.Unmap(staging,0); }
    }
    public static void Run(string output,bool quick=false)
    {
        Directory.CreateDirectory(output);
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,1280,720);
        CheckTransport(renderer);
        var settings=new OceanSettings(Celestial:OceanCelestialSettings.Default,Weather:new());
        var sunrise=new DateTimeOffset(2026,9,27,7,0,0,TimeSpan.Zero);
        renderer.Render(10,settings with {Weather=null},celestialUtc:sunrise);
        var original=SurfaceHash(renderer);
        OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,"00-approved-sunrise.png"),1280,720);
        var scenes=new[]
        {
            ("01-maritime-sunrise",sunrise,new OceanWeatherSettings()),
            ("02-cumulus-noon",sunrise.AddHours(5.5),new OceanWeatherSettings(Clouds:OceanCloudType.Cumulus,Coverage:.52f)),
            ("03-stratus-sunset",sunrise.AddHours(11),new OceanWeatherSettings(Clouds:OceanCloudType.Stratus,Coverage:.8f)),
            ("04-moon",new DateTimeOffset(2026,9,27,19,8,0,TimeSpan.Zero),new OceanWeatherSettings()),
            ("05-low-mist",sunrise,new OceanWeatherSettings(Fog:OceanFog.LowMist)),
            ("06-banks",sunrise,new OceanWeatherSettings(Fog:OceanFog.Banks)),
            ("07-clear",sunrise,new OceanWeatherSettings(Fog:OceanFog.Clear,Coverage:0)),
            ("08-moon-clear",new DateTimeOffset(2026,9,27,20,8,0,TimeSpan.Zero),new OceanWeatherSettings(Fog:OceanFog.Clear,Coverage:0)),
            ("09-moon-high",new DateTimeOffset(2026,9,27,20,8,0,TimeSpan.Zero),new OceanWeatherSettings())
        };
        foreach(var (name,utc,weather) in quick ? scenes.Take(1) : scenes)
        {
            renderer.Render(10,settings with {Weather=weather},celestialUtc:utc,weatherTime:10);
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,name+".png"),1280,720);
            if(!original.SequenceEqual(SurfaceHash(renderer))) throw new Exception("Weather changed approved spectral fields.");
            Console.WriteLine("Captured "+name);
        }
        if(quick) return;
        renderer.Render(10,settings,celestialUtc:sunrise,weatherTime:10);
        var first=renderer.Pixels(); var count=renderer.Volumes!.WorkCount;
        renderer.Render(10,settings,celestialUtc:sunrise,weatherTime:10);
        if(!first.SequenceEqual(renderer.Pixels()) || renderer.Volumes.WorkCount!=count) throw new Exception("Paused volumes change or rebuild.");
        renderer.Render(12,settings,celestialUtc:sunrise.AddHours(2),weatherTime:12);
        renderer.Render(10,settings,celestialUtc:sunrise,weatherTime:10);
        if(!first.SequenceEqual(renderer.Pixels())) throw new Exception("Weather seek is not deterministic.");
        CheckVolume(renderer,renderer.Volumes.Scatter[renderer.Volumes.CurrentIndex],false);
        CheckVolume(renderer,renderer.Volumes.Transmit[renderer.Volumes.CurrentIndex],true);
        using(var recreated=new OceanGpuRenderer(IntPtr.Zero,1280,720))
        {
            recreated.Render(10,settings,celestialUtc:sunrise,weatherTime:10);
            if(!first.SequenceEqual(recreated.Pixels())) throw new Exception("Recreated volume image differs.");
            // Cross a cache boundary through incremental preparation, then compare
            // with an independently rebuilt pair at the exact same non-grid time.
            for(var step=1;step<=23;step++)
                renderer.Render(10,settings,celestialUtc:sunrise.AddSeconds(step/30d*settings.Celestial!.TimeScale),weatherTime:10+step/30d);
            recreated.Render(10,settings,celestialUtc:sunrise.AddSeconds(23/30d*settings.Celestial!.TimeScale),weatherTime:10+23/30d);
            if(!renderer.Pixels().SequenceEqual(recreated.Pixels())) throw new Exception("Staged cache depends on frame history.");
        }
        renderer.Render(10,settings,celestialUtc:sunrise,weatherTime:70);
        if(first.SequenceEqual(renderer.Pixels())) throw new Exception("Independent wind does not move the cloud field.");
        if(!original.SequenceEqual(SurfaceHash(renderer))) throw new Exception("Wind modified approved waves.");
        foreach(var quality in Enum.GetValues<OceanQuality>())
        {
            renderer.Render(10,settings with {Quality=quality},celestialUtc:sunrise,weatherTime:10);
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,$"quality-{quality}.png"),1280,720);
        }
        var formats=new List<object>();
        foreach(var (width,height) in new[] {(1080,1920),(3440,1440),(3840,2160)})
        {
            using var format=new OceanGpuRenderer(IntPtr.Zero,width,height);
            format.Render(10,settings,celestialUtc:sunrise,weatherTime:10);
            OceanRenderChecks.Save(format.Pixels(),Path.Combine(output,$"format-{width}x{height}.png"),width,height);
            formats.Add(new {width,height,format.InternalWidth,format.InternalHeight,format.EstimatedTextureBytes});
        }
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new {renderer.AdapterName,
            gpuAnalyticTransport=true,preservedWater=true,pause=true,seek=true,recreation=true,stagedCacheDeterministic=true,
            independentWind=true,finiteHdr=true,transmissionBounds=true,formats},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine("PASS: volume scenes, waves, HDR, transmission, pause/seek/recreation, staged cache, wind, quality and display formats.");
    }
}
