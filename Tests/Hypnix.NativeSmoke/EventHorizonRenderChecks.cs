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
        byte[] Frame(double time, AethelisAudioProfile audio, VisualizerSettings? settings = null)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 640, 360, time, audio, settings ?? VisualizerSettings.Default);
            return ReadBack(renderer);
        }

        var baseline = Frame(8, silence);
        Save(baseline, Path.Combine(output, "event-horizon.png"));
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

        // Stateless shaders freeze by receiving the saved time and audio profile.
        // Compare two display regions while only the first receives new inputs.
        byte[] Pair(double movingTime, AethelisAudioProfile movingAudio)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 320, 360, movingTime, movingAudio, VisualizerSettings.Default);
            renderer.RenderViewport(320, 0, 320, 360, 8, music, VisualizerSettings.Default, freezeEffect: true);
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
}
