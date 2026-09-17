using System.IO;
using AnimatedWallPaper.Services;
using static SpectralBloomRenderChecks;

internal static class NeonRibbonsRenderChecks
{
    public static void Run(IntPtr window, string output, string shader = "NeonRibbons.hlsl", string prefix = "neon-ribbons")
    {
        using var renderer = new AethelisGpuRenderer(window, 640, 360, shader);
        var silent = new AethelisAudioProfile(0, 0, 0, 0, 0);
        var music = AethelisAudioProfile.Analyze(Enumerable.Repeat(0.04f, 64).ToArray(), 4);
        byte[] Frame(double time, AethelisAudioProfile audio, VisualizerSettings? settings = null)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 640, 360, time, audio, settings ?? VisualizerSettings.Default);
            return ReadBack(renderer);
        }
        var baseline = Frame(8, silent);
        Save(baseline, Path.Combine(output, $"{prefix}.png"));
        if (baseline.SequenceEqual(Frame(9, silent))) throw new Exception("Neon Ribbons did not animate.");
        var active = Frame(8, music);
        foreach (var settings in new[] {
            VisualizerSettings.Default with { Scale = 1.6f },
            VisualizerSettings.Default with { OffsetX = 0.4f },
            VisualizerSettings.Default with { OffsetY = -0.4f } })
        {
            if (active.SequenceEqual(Frame(8, music, settings)))
                throw new Exception($"{shader}: size/position control has no visible effect.");
        }
        Save(active, Path.Combine(output, $"{prefix}-audio.png"));
        var difference = active.Select((v, i) => i % 4 == 3 ? 0 : Math.Abs(v - baseline[i])).Sum() / (640 * 360 * 3.0);
        if (difference < 4) throw new Exception("Neon Ribbons audio response is too weak.");
        if (active.SequenceEqual(Frame(8, music, VisualizerSettings.Default with { Intensity = 8 })))
            throw new Exception("Neon Ribbons intensity lacks upper-range gain.");
        if (active.SequenceEqual(Frame(8, music, VisualizerSettings.Default with { Glow = 3 })))
            throw new Exception("Neon Ribbons glow lacks upper-range gain.");
        if (Frame(8, music, VisualizerSettings.Default with { Intensity = 0 }).Where((_, i) => i % 4 != 3).Any(v => v != 0))
            throw new Exception("Neon Ribbons zero intensity is not black.");
        renderer.BeginFrame();
        renderer.RenderViewport(0, 0, 320, 360, 8, music, VisualizerSettings.Default);
        renderer.RenderViewport(320, 0, 320, 360, 8, music, VisualizerSettings.Default, true);
        var pair = ReadBack(renderer);
        for (var y = 0; y < 360; y++)
        for (var x = 0; x < 320; x++)
        for (var c = 0; c < 3; c++)
        {
            var i = (y * 640 + x) * 4 + c;
            if (Math.Abs(pair[i] - pair[i + 320 * 4]) > 1) throw new Exception("Neon Ribbons viewport origin mismatch.");
        }
        // Custom background: a strong solid blue composites behind the effect (screen blend), so the
        // mean blue channel rises well above the plain (background-less) frame.
        var solidBackground = VisualizerSettings.Default with { Background = new VisualizerBackground("solid", "#3878E0") };
        var backgroundFrame = Frame(8, silent, solidBackground);
        Save(backgroundFrame, Path.Combine(output, $"{prefix}-background.png"));
        long plainBlue = 0, withBackgroundBlue = 0;
        for (var i = 0; i < baseline.Length; i += 4) { plainBlue += baseline[i]; withBackgroundBlue += backgroundFrame[i]; }
        if (withBackgroundBlue <= plainBlue * 2)
            throw new Exception($"{shader}: solid background not visible (plain blue={plainBlue}, with background={withBackgroundBlue}).");
        Console.WriteLine($"PASS: {shader} animation, quiet audio (difference={difference:F1}), controls, viewport origin and custom background");
    }
}
