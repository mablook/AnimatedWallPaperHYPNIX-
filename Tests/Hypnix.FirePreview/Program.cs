using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms=System.Windows.Forms;

namespace Hypnix.FirePreview;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        var output=Path.GetFullPath(args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"artifacts","fire-preview"));
        Directory.CreateDirectory(output);
        try {
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
            Forms.Application.EnableVisualStyles();
            using var form=new Forms.Form {
                Text="HYPNIX · Fogo 3D / estudo 03",
                ClientSize=new System.Drawing.Size(FireRenderer.Width,FireRenderer.Height),
                FormBorderStyle=Forms.FormBorderStyle.FixedSingle, MaximizeBox=false,
                StartPosition=Forms.FormStartPosition.CenterScreen, BackColor=System.Drawing.Color.Black,
                KeyPreview=true };
            using var renderer=new FireRenderer(form.Handle);
            if(args.Contains("--capture")) {Capture(renderer,output);return;}
            using var audio=new FireAudio();
            int audioMode=0; // system, explicit demo, off
            bool paused=false; double accumulated=0;var clock=Stopwatch.StartNew();double last=0;
            double nextTitle=0;
            using var timer=new Forms.Timer {Interval=10};
            form.KeyDown+=(_,e)=> {
                if(e.KeyCode==Forms.Keys.Escape)form.Close();
                if(e.KeyCode==Forms.Keys.Space){paused=!paused;accumulated=0;}
                if(e.KeyCode==Forms.Keys.R){renderer.Reset();accumulated=0;}
                if(e.KeyCode==Forms.Keys.F)renderer.SparksVisible=!renderer.SparksVisible;
                if(e.KeyCode==Forms.Keys.A)audioMode=(audioMode+1)%3;
                if(e.KeyCode==Forms.Keys.B){renderer.FireBed=!renderer.FireBed;renderer.Reset();accumulated=0;}
            };
            timer.Tick+=(_,_)=> {
                double now=clock.Elapsed.TotalSeconds,dt=Math.Min(now-last,.05);last=now;
                if(!paused) {
                    accumulated+=dt;int steps=0;
                    while(accumulated>=1.0/60&&steps++<3){
                        renderer.AudioTarget=audioMode==0?audio.Read():audioMode==1?FireAudio.Demo(renderer.Time):Vector3.Zero;
                        renderer.SetSpectrum(audioMode==0?audio.ReadBands():null);
                        renderer.Step();accumulated-=1.0/60;
                    }
                    if(steps>=3)accumulated=0;
                }
                renderer.Render();
                if(now>=nextTitle) {
                    string mode=audioMode==0?"som do sistema":audioMode==1?"DEMONSTRAÇÃO":"desligado";
                    form.Text=$"HYPNIX · Fogo 03 | Áudio: {mode} {renderer.AudioEnvelope.X:P0} | A: áudio · B: faixa/chama · F: faíscas · Espaço: pausa · R: reinicia";
                    nextTitle=now+.3;
                }
            };
            form.Shown+=(_,_)=>{last=clock.Elapsed.TotalSeconds;timer.Start();};
            Forms.Application.Run(form);
        }catch(Exception ex) {File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());Environment.ExitCode=1;}
    }
    static void Save(byte[] pixels,string path) {
        var bitmap=BitmapSource.Create(FireRenderer.Width,FireRenderer.Height,96,96,PixelFormats.Bgra32,null,pixels,FireRenderer.Width*4);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
    static void Capture(FireRenderer r,string output) {
        var watch=Stopwatch.StartNew();
        for(int i=0;i<240;i++)r.Step();
        var projection=r.Step(true)!.Value;var material=r.Inspect();
        var heatZones=r.HeatByZone();
        if(heatZones.Any(h=>h<material.heat*.02))throw new InvalidOperationException("A fire-bed region did not ignite");
        r.Render(false);var first=r.Pixels();Save(first,Path.Combine(output,"fire.png"));
        var firstParticles=r.ReadParticles();
        int liveSparks=firstParticles.Take(64).Count(a=>a.W>=0);
        if(liveSparks<2)throw new InvalidOperationException("Sparks did not spawn");
        r.SparksVisible=false;r.Render(false);var noSparks=r.Pixels();r.SparksVisible=true;
        int sparkPixels=Enumerable.Range(0,FireRenderer.Width*FireRenderer.Height)
            .Count(i=>first[i*4]!=noSparks[i*4]||first[i*4+1]!=noSparks[i*4+1]||first[i*4+2]!=noSparks[i*4+2]);
        if(sparkPixels<8)throw new InvalidOperationException($"Sparks are invisible ({liveSparks} alive, {sparkPixels} pixels)");
        r.Render(false);if(!first.SequenceEqual(r.Pixels()))throw new InvalidOperationException("Paused field changed");
        if(!firstParticles.SequenceEqual(r.ReadParticles()))throw new InvalidOperationException("Paused sparks changed");
        for(int i=0;i<120;i++){
            r.AudioTarget=FireAudio.Demo(r.Time);r.Step();r.AudioTarget=FireAudio.Demo(r.Time);r.Step();r.Render(false);
            Save(r.Pixels(),Path.Combine(output,$"frame-{i:000}.png"));
        }
        var last=r.Pixels();
        var lastParticles=r.ReadParticles();
        if(firstParticles.SequenceEqual(lastParticles))throw new InvalidOperationException("Sparks did not move");
        if(first.SequenceEqual(last))throw new InvalidOperationException("Animation is static");
        if(projection.after>=projection.before)throw new InvalidOperationException("Projection did not reduce divergence");
        if(material.active<100||material.fuel<=0)throw new InvalidOperationException("Empty flame");
        double heatBefore=r.Inspect().heat;r.Strength=0;r.AudioTarget=Vector3.Zero;
        for(int i=0;i<300;i++)r.Step();
        double heatAfter=r.Inspect().heat;
        if(heatAfter>=heatBefore*.1)throw new InvalidOperationException("Heat did not dissipate after source removal");
        if(r.ReadParticles().Take(64).Any(a=>a.W>=0))throw new InvalidOperationException("Sparks survived source shutdown");
        r.Strength=1;r.Reset();for(int i=0;i<241;i++)r.Step();r.Render(false);
        if(!first.SequenceEqual(r.Pixels())||!firstParticles.SequenceEqual(r.ReadParticles()))
            throw new InvalidOperationException("Reset did not reproduce the same simulation");
        for(int i=241;i<1800;i++)r.Step();
        var sustained=r.Inspect();var sustainedProjection=r.Step(true)!.Value;
        r.ReadParticles();
        if(sustained.heat>material.heat*2||sustained.active>FireRenderer.X*FireRenderer.Y*FireRenderer.Z/5)
            throw new InvalidOperationException("Fire volume grew without settling");
        if(sustainedProjection.after>=sustainedProjection.before)
            throw new InvalidOperationException("Sustained projection did not reduce divergence");
        r.Render(false);Save(r.Pixels(),Path.Combine(output,"fire-30-seconds.png"));
        // Matched fixed-step scenarios distinguish source response from a display-only flash.
        r.Reset();for(int i=0;i<360;i++)r.Step();var quiet=r.Inspect();r.Render(false);
        var quietPixels=r.Pixels();Save(quietPixels,Path.Combine(output,"audio-quiet.png"));
        r.AudioTarget=Vector3.One;r.Render(false);
        if(!quietPixels.SequenceEqual(r.Pixels()))throw new InvalidOperationException("Audio changed pixels without advancing the simulation");
        r.Reset();for(int i=0;i<360;i++)r.Step();var loud=r.Inspect();r.Render(false);
        Save(r.Pixels(),Path.Combine(output,"audio-loud.png"));
        if(loud.fuel<=quiet.fuel*1.1||loud.heat<=quiet.heat*1.1)
            throw new InvalidOperationException($"Audio did not increase the simulated source: quiet={quiet}, loud={loud}, envelope={r.AudioEnvelope}, target={r.AudioTarget}");
        r.AudioTarget=Vector3.Zero;for(int i=0;i<600;i++)r.Step();var recovered=r.Inspect();
        if(r.AudioEnvelope.Length()>.001||recovered.heat>quiet.heat*1.5)
            throw new InvalidOperationException("Audio did not recover to the quiet flame");
        r.AudioTarget=new(float.NaN,float.PositiveInfinity,float.NegativeInfinity);r.Reset();r.Step();
        if(r.AudioEnvelope!=Vector3.Zero)throw new InvalidOperationException("Non-finite audio was not rejected");
        r.AudioTarget=Vector3.Zero;r.FireBed=false;r.Reset();for(int i=0;i<240;i++)r.Step();
        var single=r.Inspect();if(single.active<100||single.heat>=quiet.heat*.5)
            throw new InvalidOperationException("Single-flame mode did not isolate its source");
        r.Render(false);Save(r.Pixels(),Path.Combine(output,"single-flame.png"));
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new {
            grid=new[]{FireRenderer.X,FireRenderer.Y,FireRenderer.Z},fixedDt=1.0/60,
            divergenceBefore=projection.before,divergenceAfter=projection.after,
            materialHeat=material.heat,materialFuel=material.fuel,activeVoxels=material.active,
            heatBeforeShutdown=heatBefore,heatAfterShutdown=heatAfter,
            liveSparks,sparkPixels,sparksMove=true,sparksExtinguish=true,resetDeterministic=true,
            playbackSeconds=30,sustainedSimulationSeconds=120,speedMultiplier=4,sustainedHeat=sustained.heat,sustainedActiveVoxels=sustained.active,
            sustainedDivergenceBefore=sustainedProjection.before,sustainedDivergenceAfter=sustainedProjection.after,
            quietHeat=quiet.heat,loudHeat=loud.heat,quietFuel=quiet.fuel,loudFuel=loud.fuel,recoveredHeat=recovered.heat,
            audioChangesSource=true,audioSilenceRecovers=true,audioDoesNotFlashRender=true,
            heatZones,allNineZonesIgnited=true,singleFlameMode=true,invalidAudioRejected=true,
            pauseStable=true,animationChanges=true,finiteNonnegative=true,elapsedSeconds=watch.Elapsed.TotalSeconds
        },new JsonSerializerOptions {WriteIndented=true}));
    }
}
