using AnimatedWallPaper.Services;
using Xunit;

namespace Hypnix.Tests;

public sealed class OceanProductTests
{
    [Fact]
    public void HeldSkyKeepsWaterAndCloudsMovingWithoutLosingVolumes()
    {
        double now=0;var runtime=new OceanRuntime(now:()=>now);var owner=new object();runtime.Activate(owner,false);
        now=10;var before=runtime.Frame().Snapshot;
        runtime.Update(before.Preferences with{DayCycle=false});now=20;var (after,settings)=runtime.Frame();
        Assert.Equal(before.SkyUtc,after.SkyUtc);Assert.Equal(20,after.WaterTime);Assert.Equal(20,after.WeatherTime);
        Assert.NotNull(settings.Celestial);Assert.NotNull(settings.Weather);
    }
    [Fact]
    public void ChangingDayRateDoesNotMoveCloudsOrJumpTheSky()
    {
        double now=0;var runtime=new OceanRuntime(now:()=>now);runtime.Activate(new object(),false);
        now=10;var before=runtime.Frame().Snapshot;
        runtime.Update(before.Preferences with{DayMinutes=10});var changed=runtime.Frame().Snapshot;
        Assert.Equal(before.SkyUtc,changed.SkyUtc);now=11;var after=runtime.Frame().Snapshot;
        Assert.Equal(144,(after.SkyUtc-before.SkyUtc).TotalSeconds);Assert.Equal(11,after.WeatherTime);
    }
    [Fact]
    public void PauseAndHostReplacementNeverRecoverPausedWallTime()
    {
        double now=0;var runtime=new OceanRuntime(now:()=>now);var a=new object();var b=new object();runtime.Activate(a,false);
        now=4;runtime.SetPaused(a,true);now=100;Assert.Equal(4,runtime.Frame().Snapshot.WaterTime);
        runtime.Activate(b,false);runtime.Release(a);now=102;Assert.Equal(6,runtime.Frame().Snapshot.WaterTime);
        runtime.Release(b);now=200;Assert.Equal(6,runtime.Frame().Snapshot.WaterTime);
    }
    [Fact]
    public void ForkHasIndependentSettingsAndClockOwnershipButPreservesPhase()
    {
        double now=0;var preview=new OceanRuntime(now:()=>now);preview.Activate(new object(),false);now=8;
        var desktop=preview.Fork();desktop.Activate(new object(),false);
        preview.Update(new OceanPreferences(Coverage:1,Wind:18));now=10;
        Assert.Equal(10,desktop.Frame().Snapshot.WaterTime);
        Assert.Equal(.25f,desktop.Frame().Snapshot.Preferences.Coverage);
        Assert.Equal(9,desktop.Frame().Snapshot.Clouds.Evaluate(10).Speed);
        Assert.Equal(1,preview.Frame().Snapshot.Preferences.Coverage);
    }
    [Fact]
    public void ChangingOnlyWindPreservesTravelAtTheEditBoundary()
    {
        double now=0;var runtime=new OceanRuntime(now:()=>now);runtime.Activate(new object(),false);now=70;
        var before=runtime.Frame().Snapshot;runtime.Update(before.Preferences with{Wind=18});var after=runtime.Frame().Snapshot;
        Assert.Equal(before.Clouds.Evaluate(70),after.Clouds.Evaluate(70));
        Assert.Equal(before.WaterTime,after.WaterTime);Assert.Equal(before.SkyUtc,after.SkyUtc);
    }
    [Fact]
    public void DraftDiscardAndApplyDuringFurtherEditsKeepCorrectRevisions()
    {
        var draft=new OceanDraft(new());draft.Edit(draft.Preferences with{Coverage= .75f});
        var submitted=draft.Preferences;var snapshot=draft.Runtime.Fork();
        draft.Edit(draft.Preferences with{Agitation=0});draft.MarkApplied(submitted,snapshot);
        Assert.True(draft.IsDirty);draft.Discard();Assert.Equal(submitted,draft.Preferences);Assert.False(draft.IsDirty);
    }
    [Fact]
    public void MomentSelectionPreservesWeatherQualityAndTemporalMode()
    {
        var draft=new OceanDraft(new(DayCycle:false,Coverage:.75f,Wind:18,Quality:OceanQuality.High));
        var before=draft.Preferences;draft.ChooseMoment(OceanMoment.Moon);
        Assert.Equal(before with{Moment=OceanMoment.Moon,SkyUtc=OceanPreferences.MomentUtc(OceanMoment.Moon)},draft.Preferences);
    }
    [Fact]
    public void HoldAfterFlowUsesCurrentMomentInsteadOfReturningToStartingCard()
    {
        var draft=new OceanDraft(new());draft.SetCycle(false);
        Assert.Null(draft.Preferences.Moment);Assert.False(draft.Preferences.DayCycle);
        Assert.Equal(draft.Runtime.Frame().Snapshot.SkyUtc,draft.Preferences.SkyUtc);
    }
    [Fact]
    public void SelectingTheSameStartingCardSeeksTheSkyWithoutResettingTheWater()
    {
        double now=0;var original=new OceanRuntime(now:()=>now);var draft=new OceanDraft(new(),original);
        draft.Runtime.Activate(new object(),false);now=80;var before=draft.Runtime.Frame().Snapshot;
        draft.ChooseMoment(OceanMoment.Dawn);var after=draft.Runtime.Frame().Snapshot;
        Assert.Equal(OceanPreferences.MomentUtc(OceanMoment.Dawn),after.SkyUtc);
        Assert.Equal(before.WaterTime,after.WaterTime);Assert.Equal(before.WeatherTime,after.WeatherTime);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void CuratedMomentsHaveAnAppropriateCelestialBody(int value)
    {
        var moment=(OceanMoment)value;
        var f=OceanCelestialModel.Evaluate(OceanPreferences.MomentUtc(moment),OceanCelestialSettings.Default);
        if(moment==OceanMoment.Moon){Assert.True(f.Sun.AirlessElevation< -6);Assert.InRange(f.Moon.AirlessElevation,3,9);Assert.True(f.PhaseAngle<.6);}
        else if(moment==OceanMoment.Day)Assert.True(f.Sun.AirlessElevation>40);
        else Assert.InRange(f.Sun.AirlessElevation,0,10);
    }
    [Fact]
    public void InvalidAndMissingPreferencesNormalizeWithoutChangingOtherDisplays()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(path,"""{"SchemaVersion":1,"DisplayWallpapers":{"one":{"WallpaperId":"ocean","Enabled":true,"Oceans":{"ocean":{"Coverage":5,"Quality":999,"DayMinutes":3}}},"disconnected":{"WallpaperId":"living-fire","Enabled":true}}}""");
            var store=new AppSettingsStore(path);var settings=store.Load();var p=settings.DisplayWallpapers["one"].Oceans["ocean"];
            Assert.Equal(1,p.Coverage);Assert.Equal(OceanQuality.Balanced,p.Quality);Assert.Equal(60,p.DayMinutes);
            Assert.Equal("living-fire",settings.DisplayWallpapers["disconnected"].WallpaperId);
            Assert.True(store.Save(settings));Assert.Equal(p,store.Load().DisplayWallpapers["one"].Oceans["ocean"]);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void OceanIsAnEnvironmentWithoutAudioControls()
    {
        var entry=new WallpaperEntry("ocean","Ocean",WallpaperKind.Ocean,null);
        Assert.False(entry.IsVisualizer);Assert.True(entry.IsEnvironment);Assert.False(entry.SupportsGlow);Assert.False(entry.SupportsColorTheme);
        var request=WallpaperCatalog.Request(entry,new());Assert.Null(request.Settings);Assert.NotNull(request.OceanPreferences);
        Assert.Equal(NativeRenderMode.Ocean,WallpaperSessionFactory.RenderMode(entry.Kind));
    }
}
