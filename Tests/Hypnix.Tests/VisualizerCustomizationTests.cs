using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class VisualizerCustomizationTests
{
    [Theory]
    [InlineData(0,0)] [InlineData(1,1)] [InlineData(-1,-1)] [InlineData(.5,-.8)] [InlineData(1,0)]
    public void CircularPadPreservesAllPositionsIncludingCorners(double x,double y)
    {
        var disk=PositionPadMapping.ToDisk(x,y);
        Assert.True(disk.X*disk.X+disk.Y*disk.Y<=1.000001);
        var restored=PositionPadMapping.FromDisk(disk.X,disk.Y);
        Assert.Equal(x,restored.X,6);Assert.Equal(y,restored.Y,6);
    }
    [Fact] public void DragOutsidePadStaysBounded()
    {
        var p=PositionPadMapping.FromDisk(8,-12);
        Assert.InRange(p.X,-1,1);Assert.InRange(p.Y,-1,1);Assert.Equal(-1,p.Y,6);
    }
    [Fact] public void CustomizationsRoundTripAndLegacyPreferencesRetainDefaults()
    {
        var file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try {
            var store=new AppSettingsStore(file);
            File.WriteAllText(file,"""{"Visualizers":{"living-fire":{"Scale":1.4}}}""");
            var old=store.Load();Assert.Null(old.Visualizers["living-fire"].Background);Assert.True(old.Visualizers["living-fire"].Sparks);
            var prefs=new VisualizerPreferences(Background:new("solid","#234567"),Sparks:false);
            old.Visualizers["living-fire"]=prefs;
            old.VisualizerPresets.Add(new("a","living-fire","Warm",prefs));
            old.VisualizerPresets.Add(new("b","lotus","Green",new(ColorTheme:2)));
            Assert.True(store.Save(old));var loaded=store.Load();
            Assert.Equal(prefs,loaded.Visualizers["living-fire"]);
            Assert.Equal(old.VisualizerPresets,loaded.VisualizerPresets);
        } finally {File.Delete(file);}
    }
    [Fact] public void InvalidPresetsAndBackgroundsAreNormalizedWithoutDiscardingOtherSettings()
    {
        var entries=new[]{new VisualizerPreset("a","living-fire"," A ",new(Scale:99)),new("a","lotus","Duplicate",new()),new("b","lotus","",new()),new("c","lotus","Future",new(),99)};
        var valid=Assert.Single(VisualizerPresetLibrary.Normalize(entries));
        Assert.Equal("A",valid.Name);Assert.Equal(3,valid.Values.Scale);
        Assert.Equal(new VisualizerBackground(),new VisualizerBackground("unknown","invalid","relative.png").Normalize());
    }
}
