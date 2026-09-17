using System.Diagnostics;
using System.Numerics;
using AnimatedWallPaper.Services;

namespace Hypnix.FirePreview;

// Reuses HYPNIX's WASAPI output capture, format decoding, FFT and endpoint recovery.
// Samples are analyzed in memory; no recording or audio file is created.
internal sealed class FireAudio : IDisposable
{
    readonly AudioSpectrumService service=new();
    readonly object sync=new();
    Vector3 latest;
    float[] latestBands=new float[64];
    long received;
    public FireAudio() {service.BandsAvailable+=OnBands;service.Start();}
    void OnBands(float[] bands) {
        var profile=AethelisAudioProfile.Analyze(bands,2.5f);
        lock(sync) {latest=new(profile.Bass,profile.Mids,profile.Highs);latestBands=bands;received=Stopwatch.GetTimestamp();}
    }
    public Vector3 Read() {
        lock(sync) return received!=0&&Stopwatch.GetElapsedTime(received).TotalSeconds<.5?latest:Vector3.Zero;
    }
    public float[] ReadBands() {
        lock(sync) return received!=0&&Stopwatch.GetElapsedTime(received).TotalSeconds<.5?(float[])latestBands.Clone():new float[64];
    }
    public static Vector3 Demo(double time) {
        // Explicit demonstration signal, never presented as captured music.
        double phase=time%2;
        float beat=(float)Math.Exp(-phase*8);
        return new(beat*.85f,(float)(.2+.17*Math.Sin(time*2.1)),beat*.55f);
    }
    public void Dispose() {service.BandsAvailable-=OnBands;service.Dispose();}
}
