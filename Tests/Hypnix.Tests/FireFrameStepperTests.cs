using AnimatedWallPaper.Services;
namespace Hypnix.Tests;
public sealed class FireFrameStepperTests
{
    [Theory]
    [InlineData(15)] [InlineData(30)] [InlineData(60)]
    public void FrameCapsAdvanceTheSameSimulationTime(int fps)
    {
        var clock=new FireFrameStepper();clock.Advance(0,false);
        int steps=0;for(int i=1;i<=fps*10;i++)steps+=clock.Advance(i/(double)fps,false);
        Assert.Equal(600,steps);
    }
    [Fact] public void MonitorPauseDoesNotCatchUpOrAffectAnotherClock()
    {
        var paused=new FireFrameStepper();var active=new FireFrameStepper();
        paused.Advance(0,false);active.Advance(0,false);
        Assert.Equal(2,paused.Advance(1d/30,false));
        for(int i=1;i<=30;i++){Assert.Equal(0,paused.Advance(1d/30,true));Assert.Equal(2,active.Advance(i/30d,false));}
        Assert.Equal(0,paused.Advance(50,false));
        Assert.Equal(2,paused.Advance(50+1d/30,false));
    }
    [Fact] public void LongGapsDuplicateAndInvalidTimestampsDoNotCreateWork()
    {
        var clock=new FireFrameStepper();clock.Advance(0,false);
        Assert.Equal(0,clock.Advance(10,false));Assert.Equal(0,clock.Advance(10,false));
        Assert.Equal(0,clock.Advance(double.NaN,false));Assert.Equal(0,clock.Advance(11,false));
        Assert.Equal(1,clock.Advance(11+1d/60,false));
    }
    [Fact] public void GalleryKeepsOldExperimentHiddenAndUsesWarmFireDefaults()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Assets","Wallpapers");
        var entries=WallpaperCatalog.LoadBuiltIns(root);
        var fire=Assert.Single(entries.Where(e=>e.Kind==WallpaperKind.LivingFire));
        Assert.True(fire.SupportsLayoutControls);Assert.Equal(1,fire.Defaults!.ColorTheme);Assert.Equal(0,fire.Defaults.Glow);
        Assert.DoesNotContain(entries,e=>e.Kind==WallpaperKind.VolumetricFire);
        Assert.Equal(NativeRenderMode.LivingFire,WallpaperSessionFactory.RenderMode(fire.Kind));
    }
}
