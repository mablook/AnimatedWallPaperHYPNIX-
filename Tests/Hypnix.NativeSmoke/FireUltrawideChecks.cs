using System.IO;
using System.Text.Json;
using AnimatedWallPaper.Services;

internal static class FireUltrawideChecks
{
    internal static void Run(IntPtr hwnd, string output)
    {
        var quiet = new AethelisAudioProfile(0, 0, 0, 0, 0);
        var settings = new VisualizerPreferences(Glow: 0, ColorTheme: 1, Sparks: false).ToSettings();
        var results = new List<Coverage>();
        // Start with the reported failure: an ultrawide display with smaller flames.
        foreach (var (width, height, scale) in new[]
        {
            (1280, 360, .5f), (640, 360, 1f), (840, 360, 1f), (1280, 360, 1f),
            (360, 640, 1f), (640, 360, .5f), (840, 360, .5f), (360, 640, .5f),
            (640, 360, .3f), (840, 360, .3f), (1280, 360, .3f), (360, 640, .3f)
        })
        {
            using var renderer = new FireGpuRenderer(hwnd, width, height);
            var prefs = settings with { Scale = scale };
            for (int frame = 0; frame <= 45; frame++)
            {
                renderer.BeginFrame();
                renderer.RenderViewport(0, 0, width, height, frame / 30d, quiet, prefs);
            }
            var pixels = renderer.Pixels();
            string fileName = FormattableString.Invariant($"living-fire-ultrawide-{width}x{height}-scale-{scale:0.0}.png");
            SpectralBloomRenderChecks.Save(pixels, Path.Combine(output, fileName), width, height);
            int uncovered = 0;
            var heights = new List<int>();
            for (int x = 0; x < width; x++)
            {
                int brightest = 0;
                for (int y = height * 3 / 4; y < height; y++)
                    brightest = Math.Max(brightest, pixels[(y * width + x) * 4 + 2]);
                if (brightest < 20) uncovered++;
                int top = height;
                for (int y = 0; y < height; y++)
                    if (pixels[(y * width + x) * 4 + 2] > 40) { top = y; break; }
                heights.Add(height - top);
            }
            int flameCount = renderer.GetSimulation(0, 0, width, height).FlameCount;
            int typicalHeight = heights.Order().ElementAt(heights.Count * 3 / 4);
            results.Add(new(width, height, scale, flameCount, uncovered, typicalHeight));
            File.WriteAllText(Path.Combine(output, "living-fire-ultrawide-checks.json"),
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Fire coverage {width}x{height} scale {scale}: {flameCount} sources, {uncovered} uncovered columns, height P75 {typicalHeight}.");
            if (uncovered != 0)
                throw new Exception($"Living Fire leaves {uncovered} columns uncovered at {width}x{height}, scale {scale}.");
        }
        var landscape = results.Where(r => r.Height == 360 && r.Scale == 1).OrderBy(r => r.Width).ToArray();
        for (int i = 1; i < landscape.Length; i++)
        {
            if (landscape[i].FlameCount <= landscape[i - 1].FlameCount)
                throw new Exception("Wider screens did not gain additional independent fire sources.");
            double relativeHeight = landscape[i].TypicalHeight / (double)landscape[0].TypicalHeight;
            if (relativeHeight is < .65 or > 1.35)
                throw new Exception($"Fire height changed with monitor width: {landscape[i].Width} / 640 = {relativeHeight:F2}.");
        }
        CheckScaleChangesKeepSimulation(hwnd, quiet, settings);
        Console.WriteLine("PASS: Living Fire covers every column at 16:9, 21:9, 32:9 and portrait, including reduced size; ultrawide adds flames while preserving their height.");
    }

    private static void CheckScaleChangesKeepSimulation(IntPtr hwnd, AethelisAudioProfile quiet, VisualizerSettings settings)
    {
        using var renderer = new FireGpuRenderer(hwnd, 640, 360);
        var spectrum = Enumerable.Range(0, 64).Select(bin => bin < 12 ? .5f : .02f).ToArray();
        void Frame(double time, float scale)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 640, 360, time, quiet, settings with { Scale = scale }, spectrum: spectrum);
        }
        Frame(0, 1);
        for (int tick = 1; tick <= 15; tick++) Frame(tick / 30d, 1);
        var simulation = renderer.GetSimulation(0, 0, 640, 360);
        double elapsed = simulation.Time;
        int previousCount = simulation.FlameCount;
        foreach (float scale in new[] { .5f, .3f })
        {
            Frame(.5, scale);
            if (!ReferenceEquals(simulation, renderer.GetSimulation(0, 0, 640, 360)) || simulation.Time != elapsed)
                throw new Exception("Changing fire size replaced, reset or advanced the running simulation.");
            if (simulation.FlameCount <= previousCount)
                throw new Exception("Reducing fire size did not add independent sources to preserve screen coverage.");
            previousCount = simulation.FlameCount;
        }
        for (int tick = 16; tick <= 45; tick++) Frame(tick / 30d, .3f);
        if (simulation.Time <= elapsed || simulation.Inspect().active == 0)
            throw new Exception("Fire stopped simulating after its source count changed.");
        Console.WriteLine("PASS: editing fire size preserves the simulation and time, grows source count and continues rendering FFT-driven fire.");
    }

    private sealed record Coverage(int Width, int Height, float Scale, int FlameCount, int UncoveredColumns, int TypicalHeight);
}
