using System.IO;
using AnimatedWallPaper.Services;
using static SpectralBloomRenderChecks;

// Aethelis is a full-screen procedural fire ring. It historically ignored the palette and the
// size/position controls; these checks prove the standardized controls now change the output.
internal static class AethelisRenderChecks
{
    public static void Run(IntPtr window, string output)
    {
        using var renderer = new AethelisGpuRenderer(window, 640, 360, "Aethelis.hlsl");
        var music = AethelisAudioProfile.Analyze(Enumerable.Repeat(0.5f, 64).ToArray(), 4);
        byte[] Frame(double time, VisualizerSettings settings)
        {
            renderer.BeginFrame();
            renderer.RenderViewport(0, 0, 640, 360, time, music, settings);
            return ReadBack(renderer);
        }

        // The Aethelis default is the warm palette so tinting keeps the approved orange look.
        var warm = new VisualizerPreferences(ColorTheme: 1).ToSettings();
        var baseline = Frame(8, warm);
        Save(baseline, Path.Combine(output, "aethelis.png"));
        if (baseline.SequenceEqual(Frame(9, warm))) throw new Exception("Aethelis did not animate.");

        foreach (var settings in new[]
        {
            warm with { Scale = 1.8f },
            warm with { OffsetX = 0.5f },
            warm with { OffsetY = -0.5f }
        })
        {
            if (baseline.SequenceEqual(Frame(8, settings)))
                throw new Exception("Aethelis size/position control has no visible effect.");
        }

        // Palette must recolor the flame: warm (default) versus the ice-blue theme.
        var iceBlue = new VisualizerPreferences(ColorTheme: 0).ToSettings();
        if (baseline.SequenceEqual(Frame(8, iceBlue)))
            throw new Exception("Aethelis color theme has no visible effect.");
        Save(Frame(8, iceBlue), Path.Combine(output, "aethelis-iceblue.png"));

        // Custom background: a strong solid blue composites behind the flame (screen blend), so the
        // mean blue channel rises well above the background-less frame.
        var withBackground = Frame(8, warm with { Background = new VisualizerBackground("solid", "#3878E0") });
        Save(withBackground, Path.Combine(output, "aethelis-background.png"));
        long baseBlue = 0, backgroundBlue = 0;
        for (var i = 0; i < baseline.Length; i += 4) { baseBlue += baseline[i]; backgroundBlue += withBackground[i]; }
        if (backgroundBlue <= baseBlue * 1.5)
            throw new Exception($"Aethelis: solid background not visible (base blue={baseBlue}, with background={backgroundBlue}).");

        // Intensity/Glow already worked before standardization and are intentionally not asserted
        // here: the Aethelis fire saturates under strong audio, so a fixed high-audio frame is not a
        // reliable gain probe. This check focuses on the standardized size/position, palette and background.
        Console.WriteLine("PASS: Aethelis animation, size/position, color theme and custom background");
    }
}
