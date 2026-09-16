using System.IO;
using AnimatedWallPaper.Services;
using static SpectralBloomRenderChecks;

internal static class EventHorizonRenderChecks
{
    public static void Run(IntPtr window, string output)
    {
        using var renderer = new AethelisGpuRenderer(window, 640, 360, "EventHorizon.hlsl");
        var silence = new AethelisAudioProfile(0, 0, 0, 0, 0);
        var music = AethelisAudioProfile.Analyze(Enumerable.Repeat(0.04f, 64).ToArray(), 4);
        byte[] Frame(double time, AethelisAudioProfile audio, VisualizerSettings? settings = null, float[]? spectrum = null)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 640, 360, time, audio, settings ?? VisualizerSettings.Default, spectrum: spectrum);
            return ReadBack(renderer);
        }

        var baseline = Frame(8, silence);
        Save(baseline, Path.Combine(output, "event-horizon.png"));
        // The old ring accumulator lit the captured region. Keep a dark shadow
        // above the foreground disk and a visible lensed arc higher in the frame.
        double PatchMean(int x0, int y0, int width, int height)
        {
            long sum = 0;
            for (var y = y0; y < y0 + height; y++)
            for (var x = x0; x < x0 + width; x++)
            for (var c = 0; c < 3; c++) sum += baseline[(y * 640 + x) * 4 + c];
            return sum / (width * height * 3.0);
        }
        var shadow = PatchMean(307, 135, 26, 20);
        var arc = PatchMean(307, 70, 26, 20);
        if (shadow > 18 || arc < shadow + 55)
            throw new Exception($"Event Horizon lost its dark shadow/lensed arc: {shadow:F2}/{arc:F2}");
        if (baseline.Where((_, i) => i % 4 != 3).Distinct().Count() < 16)
            throw new Exception("Event Horizon did not render a varied image.");
        if (baseline.SequenceEqual(Frame(9, silence)))
            throw new Exception("Event Horizon did not animate in silence.");
        var active = Frame(8, music);
        Save(active, Path.Combine(output, "event-horizon-audio.png"));
        var difference = active.Select((v, i) => i % 4 == 3 ? 0 : Math.Abs(v - baseline[i])).Sum() / (640 * 360 * 3.0);
        if (difference < 1)
            throw new Exception($"Event Horizon audio response is too weak: {difference:F2}");
        if (active.SequenceEqual(Frame(8, music, VisualizerSettings.Default with { Intensity = 8 })))
            throw new Exception("Event Horizon intensity control does not change the image.");
        if (active.SequenceEqual(Frame(8, music, VisualizerSettings.Default with { Glow = 3 })))
            throw new Exception("Event Horizon glow control does not change the image.");
        if (active.SequenceEqual(Frame(8, music, VisualizerSettings.Default with
            { StartColor = System.Drawing.Color.Lime, EndColor = System.Drawing.Color.Magenta })))
            throw new Exception("Event Horizon color controls do not change the image.");
        if (!baseline.SequenceEqual(Frame(8, silence)))
            throw new Exception("Event Horizon retained audio after returning to silence at the same time.");

        var frequencyFrames = new List<byte[]>();
        var responseRadii = new List<double>();
        foreach (var (name, start) in new[] { ("bass", 6), ("mids", 28), ("highs", 48) })
        {
            var bands = new float[64];
            Array.Fill(bands, 0.04f, start, 8);
            var pixels = Frame(8, AethelisAudioProfile.Analyze(bands, 4), spectrum: bands);
            Save(pixels, Path.Combine(output, $"event-horizon-{name}.png"));
            double total = 0, radial = 0;
            for (var y = 0; y < 360; y++)
            for (var x = 0; x < 640; x++)
            {
                var index = (y * 640 + x) * 4;
                var delta = Enumerable.Range(0, 3).Sum(c => Math.Abs(pixels[index + c] - baseline[index + c]));
                total += delta;
                radial += delta * Math.Sqrt(Math.Pow(x - 320, 2) + Math.Pow(y - 180, 2));
            }
            var mean = total / (640 * 360 * 3);
            if (mean < 0.35) throw new Exception($"Event Horizon {name} response is too weak: {mean:F2}");
            Console.WriteLine($"PASS: Event Horizon {name}: change={mean:F2}, response radius={radial / total:F1}px");
            frequencyFrames.Add(pixels);
            responseRadii.Add(radial / total);
        }
        if (responseRadii[1] < responseRadii[0] + 15 || responseRadii[2] < responseRadii[1] + 15)
            throw new Exception("Event Horizon frequencies do not progress from inner to outer disk regions.");
        if (frequencyFrames[0].SequenceEqual(frequencyFrames[1]) || frequencyFrames[1].SequenceEqual(frequencyFrames[2]))
            throw new Exception("Event Horizon frequency regions produce identical images.");

        // Equal grouped profiles must still reveal different individual FFT bands.
        var lowerMid = new float[64]; lowerMid[24] = 0.08f;
        var upperMid = new float[64]; upperMid[40] = 0.08f;
        var sameProfile = AethelisAudioProfile.Analyze(lowerMid, 4);
        if (sameProfile != AethelisAudioProfile.Analyze(upperMid, 4)) throw new Exception("Invalid equal-energy fixture.");
        if (Frame(8, sameProfile, spectrum: lowerMid).SequenceEqual(Frame(8, sameProfile, spectrum: upperMid)))
            throw new Exception("Event Horizon ignores individual frequencies within a grouped profile.");
        var zeroBands = new float[64];
        if (!baseline.SequenceEqual(Frame(8, silence, spectrum: zeroBands)))
            throw new Exception("Event Horizon FFT retained audio after silence.");
        if (!baseline.SequenceEqual(Frame(8, music, spectrum: zeroBands)))
            throw new Exception("Stale grouped audio leaked through an explicitly silent spectrum.");
        var loudSettings = VisualizerSettings.Default with { Intensity = 8, Sensitivity = 12, Glow = 3 };
        var loudSilence = Frame(8, silence, loudSettings, zeroBands);
        var loudActive = Frame(8, music, loudSettings, Enumerable.Repeat(0.04f, 64).ToArray());
        var loudDifference = loudActive.Select((v, i) => i % 4 == 3 ? 0 : Math.Abs(v - loudSilence[i])).Sum() / (640 * 360 * 3.0);
        if (loudDifference < 2) throw new Exception($"Event Horizon loses response at maximum controls: {loudDifference:F2}");
        Console.WriteLine($"PASS: Event Horizon maximum controls audio change={loudDifference:F2}");
        Save(loudActive, Path.Combine(output, "event-horizon-max-controls.png"));

        // Stateless shaders freeze by receiving the saved time and audio profile.
        // Compare two display regions while only the first receives new inputs.
        byte[] Pair(double movingTime, AethelisAudioProfile movingAudio)
        {
            var savedBands = Enumerable.Range(0, 64).Select(i => 0.015f * (i % 7)).ToArray();
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 320, 360, movingTime, movingAudio, VisualizerSettings.Default,
                spectrum: movingAudio == silence ? zeroBands : savedBands);
            renderer.RenderViewport(320, 0, 320, 360, 8, music, VisualizerSettings.Default, freezeEffect: true, spectrum: savedBands);
            return ReadBack(renderer);
        }
        var before = Pair(8, music);
        var after = Pair(9, silence);
        long movingDifference = 0;
        for (var y = 0; y < 360; y++)
        for (var x = 0; x < 320; x++)
        for (var c = 0; c < 3; c++)
        {
            var left = (y * 640 + x) * 4 + c;
            var right = left + 320 * 4;
            if (Math.Abs(before[left] - before[right]) > 1)
                throw new Exception("Event Horizon viewport origin mismatch.");
            if (before[right] != after[right])
                throw new Exception("Event Horizon frozen display changed pixels.");
            movingDifference += Math.Abs(before[left] - after[left]);
        }
        if (movingDifference == 0)
            throw new Exception("Event Horizon active display stopped with the frozen display.");
        Save(after, Path.Combine(output, "event-horizon-independent-freeze.png"));
        Console.WriteLine($"PASS: Event Horizon animation, audio response (difference={difference:F2}), controls, silence reset and independent viewport freeze");
    }

    public static void CaptureReviewFrames(IntPtr window, string output)
    {
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (3440, 1440), (720, 1280) })
        {
            using var renderer = new AethelisGpuRenderer(window, width, height, "EventHorizon.hlsl");
            var silence = new AethelisAudioProfile(0, 0, 0, 0, 0);
            byte[] Frame(double time)
            {
                renderer.BeginFrame();
                renderer.RenderViewport(0, 0, width, height, time, silence, VisualizerSettings.Default);
                return ReadBack(renderer);
            }
            var pixels = Frame(8);
            Save(pixels, Path.Combine(output, $"event-horizon-{width}x{height}.png"), width, height);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var frame = 0; frame < 8; frame++) Frame(8 + frame / 30.0);
            clock.Stop();
            Console.WriteLine($"INFO: Event Horizon {width}x{height}: {clock.Elapsed.TotalMilliseconds / 8:F2} ms/frame including GPU readback (not presentation FPS)");
            if (width == 1280)
                for (var frame = 0; frame < 48; frame++)
                    Save(Frame(8 + frame / 24.0), Path.Combine(output, $"motion-{frame:D3}.png"), width, height);
        }
    }

    // Optional, read-only loopback probe. No sound is played or recorded.
    public static void ProbeLiveAudio(string output)
    {
        using var capture = new AudioSpectrumService();
        var sync = new object();
        var frames = 0;
        var peaks = new float[3];
        capture.BandsAvailable += bands =>
        {
            lock (sync)
            {
                frames++;
                for (var i = 0; i < bands.Length; i++)
                {
                    var group = i < 16 ? 0 : i < 48 ? 1 : 2;
                    peaks[group] = Math.Max(peaks[group], bands[i]);
                }
            }
        };
        capture.Start();
        Thread.Sleep(6000);
        lock (sync)
        {
            var summary = $"Loopback probe: {frames} FFT frames; raw peaks bass={peaks[0]:F5}, mids={peaks[1]:F5}, highs={peaks[2]:F5}";
            File.WriteAllText(Path.Combine(output, "live-audio-probe.txt"), summary);
            Console.WriteLine(summary);
            if (frames == 0 || peaks.Max() < 0.0001f)
                Console.WriteLine("INCONCLUSIVE: no audible system output observed; play audio to verify live response.");
            else Console.WriteLine("PASS: live system output reaches the FFT capture service.");
        }
    }
}
