using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class OceanRefinementChecks
{
    private static byte[] ReadTexture(OceanGpuRenderer renderer, ID3D11Texture2D texture, int bytesPerPixel)
    {
        var desc = texture.Description;
        desc.BindFlags = BindFlags.None; desc.MiscFlags = ResourceOptionFlags.None;
        desc.Usage = ResourceUsage.Staging; desc.CPUAccessFlags = CpuAccessFlags.Read;
        using var staging = renderer.Device.CreateTexture2D(desc);
        renderer.Context.CopyResource(staging, texture);
        var data = new byte[desc.Width * desc.Height * bytesPerPixel];
        var mapped = renderer.Context.Map(staging, 0, MapMode.Read);
        try
        {
            for (var y = 0; y < desc.Height; y++) Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch,
                data, y * (int)desc.Width * bytesPerPixel, (int)desc.Width * bytesPerPixel);
        }
        finally { renderer.Context.Unmap(staging, 0); }
        return data;
    }
    internal static object Run(OceanGpuRenderer renderer)
    {
        using var moon = new OceanMoonTexture(renderer.Device, renderer.Context);
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Effects", "Ocean", "lroc_color_2k.jpg");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var decoded = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var expected = new byte[2048 * 1024 * 4]; decoded.CopyPixels(expected, 2048 * 4, 0);
        if (!expected.SequenceEqual(ReadTexture(renderer, moon.Texture, 4))) throw new Exception("Lunar GPU upload differs from NASA image.");
        var settings = new OceanSettings();
        renderer.Render(10.25, settings);
        var first = renderer.Pixels(); var count = renderer.Clouds!.BuildCount;
        var atmosphereCount = renderer.Atmosphere!.BuildCount;
        renderer.Render(10.25, settings);
        if (!first.SequenceEqual(renderer.Pixels()) || renderer.Clouds.BuildCount != count) throw new Exception("Paused clouds changed/rebuilt.");
        renderer.Render(10.49, settings);
        if (renderer.Clouds.BuildCount != count) throw new Exception("Cloud cache rebuilt inside its interval.");
        renderer.Render(10.50, settings);
        if (renderer.Clouds.BuildCount != count + 1) throw new Exception("Cloud boundary must build exactly one snapshot.");
        if (renderer.Atmosphere.BuildCount != atmosphereCount) throw new Exception("Moving clouds rebuilt the atmosphere.");
        var fields = ReadTexture(renderer, renderer.Clouds.Textures[0], 8);
        var partial = 0; float minT = 1, maxT = 0;
        for (var i = 0; i < fields.Length; i += 2)
        {
            var value = (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(fields, i));
            if (!float.IsFinite(value) || value < 0 || (i % 8 == 6 && value > 1)) throw new Exception("Invalid cloud HDR/transmission.");
            if (i % 8 == 6) { minT = Math.Min(minT, value); maxT = Math.Max(maxT, value); if (value > .05 && value < .95) partial++; }
        }
        if (partial < 100 || minT > .5 || maxT < .99) throw new Exception("Cloud field lacks partial coverage and clear sky.");
        renderer.Render(40.25, settings);
        var later = renderer.Pixels();
        // Compare only sky rows: proves cloud motion independently of moving water.
        if (first.AsSpan(0, renderer.Width * Math.Max(1,renderer.Height/5) * 4).SequenceEqual(
            later.AsSpan(0, renderer.Width * Math.Max(1,renderer.Height/5) * 4))) throw new Exception("Clouds did not move.");
        renderer.Render(10.25, settings);
        if (!first.SequenceEqual(renderer.Pixels())) throw new Exception("Seeking cloud time changed the deterministic formation.");
        renderer.Render(10.25, settings with { Lighting = OceanLighting.Moon }); var full = renderer.Pixels();
        renderer.Render(10.25, settings with { Lighting = OceanLighting.Moon, GibbousMoon = true });
        if (full.SequenceEqual(renderer.Pixels())) throw new Exception("Lunar phase did not affect image.");
        var partialSource=CheckPartialDisk(renderer);
        var apparentBody=CheckApparentBody(renderer);
        return new { nasaMapSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            lunarUploadExact = true, cloudHdrFinite = true, minTransmission = minT, maxTransmission = maxT,
            partiallyCoveredTexels = partial, pausedCacheStable = true, oneBuildPerBoundary = true,
            cleanAtmosphereIndependentOfWind = true, cloudMotionConfirmed = true, seekDeterministic = true, lunarPhaseChangesImage = true,
            partialSource, apparentBody };
    }
    private static object CheckApparentBody(OceanGpuRenderer renderer)
    {
        var results=new List<object>();
        foreach(var light in new[] { OceanLighting.Moon,OceanLighting.Sunset })
        {
            var settings=new OceanSettings(Lighting:light,Bloom:false,HorizonMagnification:false);
            renderer.Render(10,settings); var physical=renderer.Pixels();
            var atmosphereBuilds=renderer.Atmosphere!.BuildCount; var cloudBuilds=renderer.Clouds!.BuildCount;
            renderer.Render(10,settings with { HorizonMagnification=true }); var apparent=renderer.Pixels();
            var waterOffset=renderer.Width*renderer.Height/2*4;
            if(!physical.AsSpan(waterOffset).SequenceEqual(apparent.AsSpan(waterOffset)))
                throw new Exception("Apparent celestial size changed the approved water (bloom disabled).");
            if(atmosphereBuilds!=renderer.Atmosphere.BuildCount || cloudBuilds!=renderer.Clouds.BuildCount)
                throw new Exception("Perceptual size unnecessarily rebuilt the sky.");
            var direction=OceanLightingModel.For(light,true).Direction;
            var z=Vector3.Dot(direction,new(0,-MathF.Sin(.16f),MathF.Cos(.16f)));
            var cy=(int)(renderer.Height*(.5-Vector3.Dot(direction,new(0,MathF.Cos(.16f),MathF.Sin(.16f)))/(z*.383864)*.5));
            var cx=(int)(renderer.Width*.5+direction.X/(z*.383864)*renderer.Height*.5);
            int BrightPixels(byte[] pixels)
            {
                var count=0;
                for(var y=Math.Max(0,cy-45);y<Math.Min(renderer.Height,cy+45);y++)
                for(var x=Math.Max(0,cx-45);x<Math.Min(renderer.Width,cx+45);x++)
                {
                    var p=(y*renderer.Width+x)*4;
                    if((pixels[p]+pixels[p+1]+pixels[p+2])/3 > (light==OceanLighting.Moon ? 95 : 230)) count++;
                }
                return count;
            }
            var small=BrightPixels(physical); var large=BrightPixels(apparent);
            if(small<5 || large<small*3) throw new Exception($"Apparent disk did not enlarge: {light}, {small}/{large}.");
            results.Add(new { light=light.ToString(),physicalBrightPixels=small,apparentBrightPixels=large,waterPixelsIdentical=true,skyCacheUnchanged=true });
        }
        return results;
    }
    private static object CheckPartialDisk(OceanGpuRenderer renderer)
    {
        var settings=new OceanSettings(Lighting:OceanLighting.Moon,Bloom:false);
        renderer.Render(20,settings);
        var direction=OceanLightingModel.For(OceanLighting.Moon,true).Direction;
        var clouds=renderer.Clouds!;
        var width=clouds.Budget.Width; var height=clouds.Budget.Height;
        var sourceU=Math.Atan2(direction.X,direction.Z)/(2*Math.PI)+.5;
        void Upload(bool partial)
        {
            renderer.Context.PSSetShaderResource(12,null!); renderer.Context.PSSetShaderResource(13,null!);
            var data=new ushort[width*height*4];
            for(var y=0;y<height;y++)
            for(var x=0;x<width;x++) data[(y*width+x)*4+3]=BitConverter.HalfToUInt16Bits((Half)(partial ?
                Math.Clamp(.5-((x+.5)/width-sourceU)*width,0,1) : 1));
            var pin=GCHandle.Alloc(data,GCHandleType.Pinned);
            try
            {
                foreach(var texture in clouds.Textures) renderer.Context.UpdateSubresource(texture,0,null,pin.AddrOfPinnedObject(),(uint)width*8,0);
                renderer.Context.GenerateMips(clouds.Previous); renderer.Context.GenerateMips(clouds.Next);
            }
            finally { pin.Free(); }
        }
        Upload(false); renderer.Render(20,settings); var clear=renderer.Pixels();
        Upload(true); renderer.Render(20,settings); var covered=renderer.Pixels();
        var forward=new Vector3(0,-MathF.Sin(.16f),MathF.Cos(.16f));
        var up=new Vector3(0,MathF.Cos(.16f),MathF.Sin(.16f));
        var z=Vector3.Dot(direction,forward);
        var cx=(int)Math.Round(renderer.Width*.5+direction.X/(z*.383864)*renderer.Height*.5);
        var cy=(int)Math.Round(renderer.Height*(.5-Vector3.Dot(direction,up)/(z*.383864)*.5));
        var radius=renderer.Height*.5*OceanLightingModel.AngularRadius/(z*.383864);
        var offset=Math.Max(1,(int)(radius*.5));
        double Luminance(byte[] data,int x,int y) { var p=(y*renderer.Width+x)*4; return .0722*data[p]+.7152*data[p+1]+.2126*data[p+2]; }
        var leftRatio=Luminance(covered,cx-offset,cy)/Math.Max(1,Luminance(clear,cx-offset,cy));
        var rightRatio=Luminance(covered,cx+offset,cy)/Math.Max(1,Luminance(clear,cx+offset,cy));
        if(leftRatio-rightRatio<.15) throw new Exception("Partial source occlusion collapsed to uniform disk attenuation.");
        long waterDifference=0;
        for(var i=renderer.Width*renderer.Height*2;i<clear.Length;i++) if(i%4!=3) waterDifference+=Math.Abs(clear[i]-covered[i]);
        if(waterDifference<1000) throw new Exception("Cloud occlusion did not reach the water lighting.");
        renderer.Render(22,settings); // Replace both synthetic snapshots before other checks/captures.
        return new { leftRatio,rightRatio,waterDifference,perDirectionOcclusion=true };
    }
    internal static void Capture(string output)
    {
        using var renderer = new OceanGpuRenderer(IntPtr.Zero, 1920, 1080);
        var checks = Run(renderer);
        var settings = new OceanSettings();
        foreach (var light in Enum.GetValues<OceanLighting>())
        foreach (var quality in Enum.GetValues<OceanQuality>())
        {
            renderer.Render(10, settings with { Lighting = light, Quality = quality });
            OceanRenderChecks.Save(renderer.Pixels(), Path.Combine(output,$"{light}-{quality}.png"),1920,1080);
        }
        foreach (var phase in new[] { false, true })
        {
            renderer.Render(10, settings with { Lighting = OceanLighting.Moon, GibbousMoon = phase });
            OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,$"moon-{(phase ? "gibbous" : "full")}.png"),1920,1080);
        }
        renderer.Render(10, settings with { Bloom = false });
        OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,"sunset-no-bloom.png"),1920,1080);
        renderer.Render(10, settings with { RefinedSky = false });
        OceanRenderChecks.Save(renderer.Pixels(),Path.Combine(output,"sunset-p3-reference.png"),1920,1080);
        using var high = new OceanGpuRenderer(IntPtr.Zero,3840,2160);
        foreach (var phase in new[] { false, true })
        {
            high.Render(10,settings with { Lighting = OceanLighting.Moon, GibbousMoon = phase, Quality = OceanQuality.High });
            OceanRenderChecks.Save(high.Pixels(),Path.Combine(output,$"moon-4k-{(phase ? "gibbous" : "full")}.png"),3840,2160);
        }
        File.WriteAllText(Path.Combine(output,"refinement-checks.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine("PASS: NASA GPU texture, volumetric HDR/transmission, deterministic cache, cloud motion and lunar phase.");
    }

    internal static void Movie(string output, string encoder)
    {
        var start = new ProcessStartInfo(encoder) { UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardError=true };
        foreach(var arg in new[] { "-hide_banner","-loglevel","error","-y","-f","rawvideo","-pixel_format","bgra",
            "-video_size","960x540","-framerate","30","-i","pipe:0","-an","-c:v","libx264","-preset","fast","-crf","19",
            "-pix_fmt","yuv420p","-movflags","+faststart",Path.Combine(output,"ocean-refinement-60s.mp4") }) start.ArgumentList.Add(arg);
        using var ffmpeg=Process.Start(start) ?? throw new Exception("Cannot start the supplied video encoder.");
        var errors=ffmpeg.StandardError.ReadToEndAsync();
        using var renderer=new OceanGpuRenderer(IntPtr.Zero,960,540);
        byte[]? paused=null;
        try
        {
            for(var i=0;i<1800;i++)
            {
                var time=i<300 ? 10+i/30d : i<360 ? 20 : 10+(i-60)/30d;
                renderer.Render(time,new OceanSettings(Lighting:i<900 ? OceanLighting.Sunset : OceanLighting.Moon));
                var pixels=renderer.Pixels();
                if(i==300) paused=pixels;
                if(i>300 && i<360 && !paused!.SequenceEqual(pixels)) throw new Exception("Animation capture moved while paused.");
                ffmpeg.StandardInput.BaseStream.Write(pixels);
                if(i%300==0 || i==1799) OceanRenderChecks.Save(pixels,Path.Combine(output,$"motion-{i:0000}.png"),960,540);
            }
            ffmpeg.StandardInput.Close();
            if(!ffmpeg.WaitForExit(60000)) throw new TimeoutException("Video encoder did not finish.");
            var error=errors.GetAwaiter().GetResult();
            if(ffmpeg.ExitCode!=0) throw new Exception(error);
        }
        finally { if(!ffmpeg.HasExited) ffmpeg.Kill(); }
        Console.WriteLine("PASS: 60 seconds at 30 FPS, sunset and moon, two-second pause pixel-exact, resumed logical time.");
    }
}
