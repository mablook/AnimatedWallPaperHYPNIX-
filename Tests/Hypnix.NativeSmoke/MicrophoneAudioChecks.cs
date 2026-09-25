using System.IO;
using AnimatedWallPaper.Services;

internal static class MicrophoneAudioChecks
{
    // Explicit local probe: retain only a frame count and peak band, never raw audio.
    public static void Probe(string output)
    {
        var sync = new object();
        var frames = 0;
        var activeFrames = 0;
        float peak = 0;
        using var microphone = new AudioSpectrumService(microphone: true);
        microphone.BandsAvailable += bands =>
        {
            lock (sync)
            {
                // Ignore lifecycle reset frames; count silence from an open device separately.
                if (!microphone.IsCapturing) return;
                frames++;
                if (bands.Any(value => value > 0)) activeFrames++;
                peak = Math.Max(peak, bands.Max());
            }
        };
        microphone.Start();
        Thread.Sleep(TimeSpan.FromSeconds(5));
        microphone.Dispose();
        if (!microphone.Completion.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Microphone endpoint did not finish closing after disable.");
        string result;
        lock (sync)
            result = activeFrames > 0
                ? $"PASS: live microphone produced {activeFrames} nonzero FFT frames of {frames}; peak band {peak:F4}; endpoint closed. No audio saved."
                : frames > 0
                    ? $"PARTIAL: microphone opened and analyzed {frames} silent FFT frames; endpoint closed. Voice reaction unverified. No audio saved."
                    : "INCONCLUSIVE: microphone unavailable or blocked; no captured FFT frames; endpoint worker closed. No audio saved.";
        File.WriteAllText(Path.Combine(output, "microphone-probe.txt"), result);
        Console.WriteLine(result);
    }
}
