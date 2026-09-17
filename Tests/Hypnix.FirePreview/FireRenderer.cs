using System.Numerics;
using AnimatedWallPaper.Services;
namespace Hypnix.FirePreview;

// The study harness now exercises the same renderer shipped in the wallpaper gallery.
internal sealed class FireRenderer : IDisposable
{
    internal const int Width=FireSimulation.Width,Height=FireSimulation.Height,X=FireSimulation.X,Y=FireSimulation.Y,Z=FireSimulation.Z;
    readonly FireGpuRenderer renderer;
    readonly FireSimulation simulation;
    public FireRenderer(IntPtr hwnd) {renderer=new(hwnd,Width,Height);simulation=renderer.GetSimulation(0,0,Width,Height);}
    public float Strength {get=>simulation.Strength;set=>simulation.Strength=value;}
    public bool SparksVisible {get=>simulation.SparksVisible;set=>simulation.SparksVisible=value;}
    public bool FireBed {get=>simulation.FireBed;set=>simulation.FireBed=value;}
    public Vector3 AudioTarget {get=>simulation.AudioTarget;set=>simulation.AudioTarget=value;}
    public Vector3 AudioEnvelope=>simulation.AudioEnvelope;
    public void SetSpectrum(float[]? bands) {
        if(bands is null)simulation.UseGroupedAudio();
        else simulation.SetFrequencyTargets(FireFrequencyBands.Analyze(bands,2.5f,simulation.FlameCount));
    }
    public double Time=>simulation.Time;
    public void Reset()=>simulation.Reset();
    public (double before,double after)? Step(bool measure=false)=>simulation.Step(measure);
    public void Render(bool present=true)=>renderer.RenderPreview(present);
    public byte[] Pixels()=>renderer.Pixels();
    public Vector4[] ReadParticles()=>simulation.ReadParticles();
    public (double heat,double fuel,int active) Inspect()=>simulation.Inspect();
    public double[] HeatByZone()=>simulation.HeatByZone();
    public void Dispose()=>renderer.Dispose();
}
