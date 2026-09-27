using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using AnimatedWallPaper.Services;
using Vortice.Direct3D11;

internal static class OceanRenderChecks
{
    public static void Run(string output, bool frames)
    {
        const int width = 960, height = 540;
        using var renderer = new OceanGpuRenderer(IntPtr.Zero, width, height);
        var spectrumValidation = OceanSpectrumChecks.Run(renderer.Device, renderer.Context);
        var lightingValidation = OceanLightingChecks.Run(renderer);
        var settings = new OceanSettings();
        renderer.Render(10, settings);
        var first = renderer.Pixels();
        renderer.Render(10, settings);
        if (!first.SequenceEqual(renderer.Pixels())) throw new Exception("Ocean is not deterministic while paused.");
        renderer.Render(10 + 1d / 30, settings);
        if (first.SequenceEqual(renderer.Pixels())) throw new Exception("Ocean is not animated.");
        foreach (var light in Enum.GetValues<OceanLighting>())
        foreach (var (strength, name) in new[] { (0f, "calm"), (.65f, "moderate"), (1f, "rough") })
        {
            renderer.Render(10, settings with { Lighting = light, Agitation = strength });
            var pixels = renderer.Pixels();
            var rgb = pixels.Where((_, i) => i % 4 != 3).Select(x => (double)x).ToArray();
            if (rgb.Average() < 2 || rgb.Max() - rgb.Min() < 40) throw new Exception($"Empty or flat ocean: {light}/{name}");
            Save(pixels, Path.Combine(output, $"ocean-{light.ToString().ToLowerInvariant()}-{name}.png"), width, height);
            renderer.Render(10,settings with { Lighting=light,Agitation=strength,Horizon=false });
            Save(renderer.Pixels(),Path.Combine(output,$"ocean-{light.ToString().ToLowerInvariant()}-{name}-close.png"),width,height);
        }
        renderer.Render(10, settings with { Horizon = false });
        Save(renderer.Pixels(), Path.Combine(output, "ocean-close.png"), width, height);
        foreach (var light in Enum.GetValues<OceanLighting>())
        {
            renderer.Render(10, settings with { Lighting = light, Horizon = false });
            Save(renderer.Pixels(), Path.Combine(output, $"ocean-{light.ToString().ToLowerInvariant()}-close.png"), width, height);
            renderer.Render(10, settings with { Lighting = light, Atmosphere = false });
            Save(renderer.Pixels(), Path.Combine(output, $"ocean-{light.ToString().ToLowerInvariant()}-previous-light.png"), width, height);
        }
        renderer.Render(10, settings with { Surface = OceanSurface.Analytic });
        Save(renderer.Pixels(), Path.Combine(output, "ocean-analytic-comparison.png"), width, height);
        renderer.Render(10, settings);
        if (!first.SequenceEqual(renderer.Pixels())) throw new Exception("Switching comparison model changed the spectral phase.");
        renderer.Render(36000, settings);
        Save(renderer.Pixels(), Path.Combine(output, "ocean-10-hours.png"), width, height);

        // Verify presentation through the real GDI bridge, including non-aligned row pitch.
        using (var odd = new OceanGpuRenderer(IntPtr.Zero, 213, 121))
        using (var bitmap = new Bitmap(213, 121, PixelFormat.Format32bppRgb))
        {
            odd.Render(10, settings);
            var expected = odd.Pixels();
            using (var g = Graphics.FromImage(bitmap))
            {
                var dc = g.GetHdc();
                try { odd.PreviewSurface!.CopyToDeviceContext(dc); }
                finally { g.ReleaseHdc(dc); }
            }
            for (var y = 0; y < 121; y++)
            for (var x = 0; x < 213; x++)
            {
                var pixel = bitmap.GetPixel(x, y); var offset = (y * 213 + x) * 4;
                if (pixel.B != expected[offset] || pixel.G != expected[offset + 1] || pixel.R != expected[offset + 2])
                    throw new Exception("Ocean preview GDI differs from the GPU target.");
            }
        }
        var hidden = CreateWindowEx(0, "STATIC", "Ocean DXGI test", 0x80000000,
            0, 0, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hidden == IntPtr.Zero) throw new Exception("Cannot create hidden Ocean DXGI surface.");
        try
        {
            using var desktop = new OceanGpuRenderer(hidden, width, height, preview: false);
            desktop.Render(10, settings);
            if (!first.SequenceEqual(desktop.Pixels())) throw new Exception("Ocean DXGI and offscreen outputs differ.");
            desktop.Present();
        }
        finally { DestroyWindow(hidden); }
        foreach (var (w, h, name) in new[] { (540, 960, "portrait"), (1280, 360, "ultrawide"), (3840, 2160, "4k") })
        {
            using var sized = new OceanGpuRenderer(IntPtr.Zero, w, h);
            sized.Render(10, settings with { Quality = OceanQuality.High });
            Save(sized.Pixels(), Path.Combine(output, $"ocean-{name}.png"), w, h);
            if (w == 3840)
            {
                sized.Render(10, settings with { Quality = OceanQuality.Economy });
                if (sized.InternalWidth != 1280 || sized.InternalHeight != 720) throw new Exception("Ocean quality budget was not applied.");
                sized.Render(10, settings with { Quality = OceanQuality.High });
                using var recreated = new OceanGpuRenderer(IntPtr.Zero, w, h);
                recreated.Render(10, settings with { Quality = OceanQuality.High });
                if (!sized.Pixels().SequenceEqual(recreated.Pixels())) throw new Exception("Ocean changed phase after GPU recreation/quality change.");
            }
        }
        var gpuTimes = MeasureGpu(renderer, settings);
        var previousLightGpuTimes = MeasureGpu(renderer, settings with { Atmosphere = false });
        if (frames)
        {
            var directory = Path.Combine(output, "frames"); Directory.CreateDirectory(directory);
            for (var i = 0; i < 180; i++)
            {
                renderer.Render(10 + i / 30d, settings);
                Save(renderer.Pixels(), Path.Combine(directory, $"ocean-{i:0000}.png"), width, height);
            }
        }
        File.WriteAllText(Path.Combine(output, "ocean-checks.json"), JsonSerializer.Serialize(new
        {
            stage = "R1-R6 refined sky, real lunar map and cached volumetric clouds over approved P2 surface; pending visual acceptance",
            renderer.AdapterName, width, height, renderer.InternalWidth, renderer.InternalHeight,
            renderer.EstimatedTextureBytes, spectralBands = 3, spectralResolution = 256,
            spectrumValidation, lightingValidation, pauseDeterministic = true,
            animated = true, gdiMatchesGpu = true, dxgiMatchesOffscreen = true, recreationPreservesPhase = true,
            qualityResourceResize = true, scenes = 24, portrait = true, ultrawide = true, native4kCapture = true,
            gpuSampleCount = gpuTimes.Length, gpuMedianMs = Percentile(gpuTimes, .5), gpuP95Ms = Percentile(gpuTimes, .95),
            previousLightGpuSampleCount = previousLightGpuTimes.Length,
            previousLightGpuMedianMs = Percentile(previousLightGpuTimes, .5), previousLightGpuP95Ms = Percentile(previousLightGpuTimes, .95),
            timingScope = "Short asynchronous-query probe; no presentation/readback; not the 120-second acceptance benchmark."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: Ocean sky refinement, unchanged wave fields, sky cache and finite HDR, GPU IFFT vs direct DFT, 24 scenes, GDI/DXGI, 4K. {renderer.AdapterName}. GPU p95 {Percentile(gpuTimes, .95):F2} ms ({gpuTimes.Length} samples, 960x540). ");
    }

    internal static void Save(byte[] pixels, string path, int width, int height) =>
        SpectralBloomRenderChecks.Save(pixels, path, width, height);

    [StructLayout(LayoutKind.Sequential)] private struct ClockData { public ulong Frequency; public int Disjoint; }
    private sealed class Sample : IDisposable
    {
        public readonly ID3D11Query Clock, Begin, End;
        public bool Pending;
        public Sample(ID3D11Device device)
        {
            Clock = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
            Begin = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
            End = device.CreateQuery(new QueryDescription(QueryType.Timestamp));
        }
        public void Dispose() { End.Dispose(); Begin.Dispose(); Clock.Dispose(); }
    }
    private static bool TryRead<T>(ID3D11DeviceContext context, ID3D11Query query, out T value) where T : struct
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            var result = context.GetData(query, memory, (uint)Marshal.SizeOf<T>(), AsyncGetDataFlags.DoNotFlush);
            result.CheckError();
            value = result.Code == 0 ? Marshal.PtrToStructure<T>(memory) : default;
            return result.Code == 0;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static double[] MeasureGpu(OceanGpuRenderer renderer, OceanSettings settings)
    {
        for (var i = 0; i < 30; i++) renderer.Render(i / 30d, settings);
        var samples = Enumerable.Range(0, 12).Select(_ => new Sample(renderer.Device)).ToArray();
        var results = new List<double>();
        var context = renderer.Context;
        void Collect()
        {
            foreach (var sample in samples.Where(s => s.Pending))
            {
                if (!TryRead(context, sample.Clock, out ClockData clock) ||
                    !TryRead(context, sample.Begin, out ulong start) || !TryRead(context, sample.End, out ulong end)) continue;
                if (clock.Disjoint == 0 && clock.Frequency > 0) results.Add((end - start) * 1000d / clock.Frequency);
                sample.Pending = false;
            }
        }
        try
        {
            for (var i = 0; i < 120; i++)
            {
                Collect();
                var sample = samples.FirstOrDefault(s => !s.Pending);
                if (sample is not null) { context.Begin(sample.Clock); context.End(sample.Begin); }
                renderer.Render(10 + i / 30d, settings);
                if (sample is not null) { context.End(sample.End); context.End(sample.Clock); sample.Pending = true; }
                // Flush submits work but never polls/waits for completion per frame.
                context.Flush();
            }
            var deadline = Stopwatch.StartNew();
            while (samples.Any(s => s.Pending) && deadline.Elapsed.TotalSeconds < 10) { Collect(); Thread.Sleep(1); }
            if (samples.Any(s => s.Pending) || results.Count == 0) throw new Exception("Ocean GPU timestamp probe did not complete.");
            return results.Order().ToArray();
        }
        finally { foreach (var sample in samples) sample.Dispose(); }
    }
    private static double Percentile(double[] sorted, double p) => sorted[(int)Math.Floor((sorted.Length - 1) * p)];

    internal static void Benchmark(string output)
    {
        const int width=1920, height=1080;
        var hidden=CreateWindowEx(0,"STATIC","Ocean presentation benchmark",0x80000000,0,0,width,height,
            IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(hidden==IntPtr.Zero) throw new Exception("Cannot create benchmark presentation target.");
        try
        {
            var cold=Stopwatch.StartNew();
            using var pacer=new OceanFramePacer();
            using var renderer=new OceanGpuRenderer(hidden,width,height);
            var settings=new OceanSettings();
            renderer.Render(0,settings,true);
            var coldMs=cold.Elapsed.TotalMilliseconds;
            var context=renderer.Context;
            var queries=Enumerable.Range(0,12).Select(_=>new Sample(renderer.Device)).ToArray();
            var cached=new Dictionary<Sample,bool>();
            var all=new List<object>();
            try
            {
                var warm=Stopwatch.StartNew();
                while(warm.Elapsed.TotalSeconds<30) { var start=Stopwatch.GetTimestamp(); renderer.Render(warm.Elapsed.TotalSeconds,settings,true); pacer.Wait(1000d/30-Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
                for(var run=0;run<3;run++)
                {
                    var frameMs=new List<double>(); var intervals=new List<double>();
                    var gpu=new List<double>(); var updates=new List<double>();
                    var process=Process.GetCurrentProcess(); var cpuStart=process.TotalProcessorTime;
                    var stopwatch=Stopwatch.StartNew(); double previous=-1; var droppedQueries=0;
                    void Collect()
                    {
                        foreach(var sample in queries.Where(q=>q.Pending))
                        {
                            if(!TryRead(context,sample.Clock,out ClockData clock) || !TryRead(context,sample.Begin,out ulong a) || !TryRead(context,sample.End,out ulong b)) continue;
                            if(clock.Disjoint==0 && clock.Frequency>0)
                            {
                                var ms=(b-a)*1000d/clock.Frequency; gpu.Add(ms); if(cached[sample]) updates.Add(ms);
                            }
                            sample.Pending=false;
                        }
                    }
                    while(stopwatch.Elapsed.TotalSeconds<120)
                    {
                        var start=stopwatch.Elapsed.TotalMilliseconds;
                        if(previous>=0) intervals.Add(start-previous); previous=start;
                        Collect(); var sample=queries.FirstOrDefault(q=>!q.Pending);
                        if(sample is not null) { context.Begin(sample.Clock); context.End(sample.Begin); } else droppedQueries++;
                        var builds=renderer.Clouds!.BuildCount;
                        renderer.Render(30+run*120+start/1000,settings);
                        if(sample is not null) { context.End(sample.End); context.End(sample.Clock); sample.Pending=true; cached[sample]=renderer.Clouds.BuildCount!=builds; }
                        renderer.Present();
                        var elapsed=stopwatch.Elapsed.TotalMilliseconds-start; frameMs.Add(elapsed);
                        pacer.Wait(1000d/30-elapsed);
                    }
                    context.Flush();
                    var drain=Stopwatch.StartNew();
                    while(queries.Any(q=>q.Pending) && drain.Elapsed.TotalSeconds<10) { Collect(); Thread.Sleep(1); }
                    if(queries.Any(q=>q.Pending) || gpu.Count<frameMs.Count*.99) throw new Exception("Incomplete GPU benchmark sample coverage.");
                    frameMs.Sort(); intervals.Sort(); gpu.Sort(); updates.Sort();
                    var duration=stopwatch.Elapsed.TotalSeconds;
                    var cpu=(process.TotalProcessorTime-cpuStart).TotalSeconds/duration/Environment.ProcessorCount*100;
                    var result=new { run=run+1, seconds=duration, frames=frameMs.Count, fps=frameMs.Count/duration,
                        cpuMachinePercent=cpu, gpuSamples=gpu.Count, cloudUpdateSamples=updates.Count,droppedQueries,
                        gpuMedianMs=Percentile(gpu.ToArray(),.5),gpuP95Ms=Percentile(gpu.ToArray(),.95),gpuP99Ms=Percentile(gpu.ToArray(),.99),gpuMaxMs=gpu.Max(),
                        updateGpuMedianMs=Percentile(updates.ToArray(),.5),updateGpuMaxMs=updates.Max(),
                        frameWithGdiP95Ms=Percentile(frameMs.ToArray(),.95),frameWithGdiMaxMs=frameMs.Max(),
                        intervalP95Ms=Percentile(intervals.ToArray(),.95),intervalP99Ms=Percentile(intervals.ToArray(),.99),
                        workingSetBytes=process.WorkingSet64,logicalTextureBytes=renderer.EstimatedTextureBytes };
                    all.Add(result);
                    File.WriteAllText(Path.Combine(output,"benchmark.json"),JsonSerializer.Serialize(new { renderer.AdapterName,width,height,
                        quality="Balanced",targetFps=30,warmupSeconds=30,coldFirstPresentMs=coldMs,runs=all,
                        scope="Offscreen native rendering plus GPU readback/GDI into one hidden HWND. No visible UI/desktop compositor cost. GPU queries cover all passes including cloud refresh. Not energy/temperature measurement." },new JsonSerializerOptions { WriteIndented=true }));
                    Console.WriteLine($"Benchmark {run+1}/3: {result.fps:F2} FPS; GPU p95 {result.gpuP95Ms:F3} ms; cache max {result.updateGpuMaxMs:F2} ms; GDI frame p95 {result.frameWithGdiP95Ms:F2} ms.");
                    if(result.fps<28 || result.intervalP95Ms>40) throw new Exception("Ocean balanced benchmark missed the planned frame pacing budget.");
                }
            }
            finally { foreach(var q in queries) q.Dispose(); }
        }
        finally { DestroyWindow(hidden); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
