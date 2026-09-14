using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class PreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hypnix-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void PreferencesRoundTripWithIndependentWallpaperSettings()
    {
        var store = new AppSettingsStore(FilePath);
        var settings = new AppSettings { FramesPerSecond = 60, SelectedWallpaperId = "a", PauseOnBattery = true };
        settings.Visualizers["a"] = new(2.1f, 1.5f, 0.8f, 2);
        settings.Visualizers["b"] = new(0.5f, 0.4f, 0.1f, 1);
        settings.Videos.Add(new("video:1", Path.Combine(_directory, "clip.mp4"), "Clip"));
        Assert.True(store.Save(settings));
        var restored = store.Load();
        Assert.Equal(60, restored.FramesPerSecond);
        Assert.True(restored.PauseOnBattery);
        Assert.Equal(settings.Visualizers["a"], restored.Visualizers["a"]);
        Assert.Equal(settings.Visualizers["b"], restored.Visualizers["b"]);
        Assert.Equal(settings.Videos, restored.Videos);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":99}")]
    public void CorruptOrUnsupportedPreferencesUseDefaults(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
        Assert.Equal(30, new AppSettingsStore(FilePath).Load().FramesPerSecond);
    }

    [Fact]
    public void OutOfRangeSettingsAreBoundedOnLoad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"FramesPerSecond":999,"AppPauseMode":99,"Visualizers":{"a":{"Intensity":-50,"Glow":100}}}""");
        var settings = new AppSettingsStore(FilePath).Load();
        Assert.Equal(30, settings.FramesPerSecond);
        Assert.Equal(2, settings.AppPauseMode);
        Assert.Equal(0.35f, settings.Visualizers["a"].Intensity);
        Assert.Equal(1, settings.Visualizers["a"].Glow);
    }

    [Theory]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(1080, 1920, 404, 720)]
    [InlineData(2560, 1080, 1280, 540)]
    public void VideoKeepsItsAspectBeforePerDisplayCrop(int w, int h, int expectedW, int expectedH)
        => Assert.Equal((expectedW, expectedH), MediaTools.CalculateDecodeSize(w, h));

    [Fact]
    public void RotatedPhoneVideoUsesPortraitDisplayDimensions()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""{"width":1920,"height":1080,"side_data_list":[{"rotation":-90}]}""");
        Assert.Equal((404, 720), MediaTools.ReadDisplayDimensions(json.RootElement));
    }

    [Fact]
    public void AnamorphicVideoUsesDisplayAspectInsteadOfStorageAspect()
    {
        using var json = System.Text.Json.JsonDocument.Parse("""{"width":720,"height":576,"sample_aspect_ratio":"16:15"}""");
        Assert.Equal((768, 576), MediaTools.ReadDisplayDimensions(json.RootElement));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
