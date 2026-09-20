using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class DisplayPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hypnix-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void SameWallpaperKeepsIndependentSettingsForEachDisplayAcrossRestarts()
    {
        var store = new AppSettingsStore(FilePath);
        var settings = new AppSettings { SelectedWallpaperId = "browsing-another-wallpaper" };
        settings.DisplayWallpapers["monitor:left"] = new()
        {
            WallpaperId = "living-fire",
            Enabled = true,
            Visualizers = new() { ["living-fire"] = new(4, 5, 1.5f, 1, 1.4f, 0.3f, -0.2f) }
        };
        settings.DisplayWallpapers["monitor:right"] = new()
        {
            WallpaperId = "living-fire",
            Enabled = true,
            Visualizers = new() { ["living-fire"] = new(2, 3, 0.8f, 2, 0.7f, -0.5f, 0.4f) }
        };

        Assert.True(store.Save(settings));
        var restored = store.Load();
        Assert.Equal("browsing-another-wallpaper", restored.SelectedWallpaperId);
        Assert.Equal(settings.DisplayWallpapers["monitor:left"].Visualizers["living-fire"],
            restored.DisplayWallpapers["monitor:left"].Visualizers["living-fire"]);
        Assert.Equal(settings.DisplayWallpapers["monitor:right"].Visualizers["living-fire"],
            restored.DisplayWallpapers["monitor:right"].Visualizers["living-fire"]);

        restored.DisplayWallpapers["monitor:left"].Visualizers["living-fire"] = new(ColorTheme: 3);
        Assert.True(store.Save(restored));
        var afterEdit = store.Load();
        Assert.Equal(3, afterEdit.DisplayWallpapers["monitor:left"].Visualizers["living-fire"].ColorTheme);
        Assert.Equal(settings.DisplayWallpapers["monitor:right"].Visualizers["living-fire"],
            afterEdit.DisplayWallpapers["monitor:right"].Visualizers["living-fire"]);
        Assert.Empty(afterEdit.Visualizers);
    }

    [Fact]
    public void DisconnectedDisplayAssignmentsAreRetainedAndDeviceIdLookupIgnoresCase()
    {
        var store = new AppSettingsStore(FilePath);
        var settings = new AppSettings
        {
            Displays = [new("monitor:connected", "DISPLAY1", 0, 0, 1920, 1080, 96, 96)]
        };
        settings.DisplayWallpapers["MONITOR:DISCONNECTED"] = new()
        {
            WallpaperId = "event-horizon",
            Enabled = true,
            Visualizers = new() { ["event-horizon"] = new(Scale: 1.6f) }
        };

        Assert.True(store.Save(settings));
        var restored = store.Load();
        Assert.Single(restored.Displays);
        var disconnected = restored.DisplayWallpapers["monitor:disconnected"];
        Assert.True(disconnected.Enabled);
        Assert.Equal("event-horizon", disconnected.WallpaperId);
        Assert.Equal(1.6f, disconnected.Visualizers["event-horizon"].Scale);
        Assert.True(store.Save(restored));
        Assert.True(store.Load().DisplayWallpapers.ContainsKey("MONITOR:DISCONNECTED"));
    }

    [Fact]
    public void LegacySettingsKeepGlobalFallbackWithoutCreatingOrEnablingDisplayAssignments()
    {
        WriteJson("""
            {
              "SchemaVersion": 1,
              "SelectedWallpaperId": "living-fire",
              "Visualizers": { "living-fire": { "Intensity": 4.5, "ColorTheme": 2 } },
              "Displays": [
                { "DeviceId": "monitor:old", "DeviceName": "DISPLAY1", "Width": 1920, "Height": 1080 }
              ]
            }
            """);

        var restored = new AppSettingsStore(FilePath).Load();
        Assert.Equal(1, restored.SchemaVersion);
        Assert.Equal("living-fire", restored.SelectedWallpaperId);
        Assert.Equal(4.5f, restored.Visualizers["living-fire"].Intensity);
        Assert.Equal(2, restored.Visualizers["living-fire"].ColorTheme);
        Assert.Empty(restored.DisplayWallpapers);
        Assert.Single(restored.Displays);
        Assert.False(new DisplayWallpaperSettings().Enabled);
    }

    [Fact]
    public void MalformedEntriesAreNormalizedWithoutDiscardingOtherDisplayAssignments()
    {
        WriteJson("""
            {
              "SelectedWallpaperId": " ",
              "DisplayWallpapers": {
                "": { "Enabled": true },
                "   ": { "Enabled": true },
                "null-entry": null,
                "monitor:empty": { "WallpaperId": null, "Visualizers": null },
                "monitor:valid": {
                  "WallpaperId": "living-fire", "Enabled": true,
                  "Visualizers": {
                    "": {}, " ": {}, "null-value": null,
                    "living-fire": {
                      "Intensity": -10, "Sensitivity": 50, "Glow": 90,
                      "ColorTheme": 20, "Scale": 10, "OffsetX": -10, "OffsetY": 5
                    }
                  }
                }
              },
              "Visualizers": { "": {}, "null-value": null, "fallback": { "Glow": -3 } }
            }
            """);

        var restored = new AppSettingsStore(FilePath).Load();
        Assert.Equal("built-in-ambient", restored.SelectedWallpaperId);
        Assert.Equal(2, restored.DisplayWallpapers.Count);
        Assert.Equal("built-in-ambient", restored.DisplayWallpapers["monitor:empty"].WallpaperId);
        Assert.Empty(restored.DisplayWallpapers["monitor:empty"].Visualizers);
        Assert.False(restored.DisplayWallpapers["monitor:empty"].Enabled);
        var valid = restored.DisplayWallpapers["monitor:valid"];
        Assert.True(valid.Enabled);
        Assert.Single(valid.Visualizers);
        Assert.Equal(new VisualizerPreferences(0, 12, 3, 3, 3, -1, 1), valid.Visualizers["living-fire"]);
        Assert.Single(restored.Visualizers);
        Assert.Equal(0, restored.Visualizers["fallback"].Glow);
    }

    [Fact]
    public void NullDisplayDictionaryLoadsAsEmptyCaseInsensitiveDictionary()
    {
        WriteJson("""{ "DisplayWallpapers": null }""");
        var restored = new AppSettingsStore(FilePath).Load();
        Assert.Empty(restored.DisplayWallpapers);
        restored.DisplayWallpapers["MONITOR:NEW"] = new();
        Assert.True(restored.DisplayWallpapers.ContainsKey("monitor:new"));
    }

    [Fact]
    public void DeviceIdsThatDifferOnlyByCaseCollapseToOneAssignmentWithoutResettingSettings()
    {
        WriteJson("""
            {
              "FramesPerSecond": 60,
              "DisplayWallpapers": {
                "MONITOR:ONE": { "WallpaperId": "living-fire", "Enabled": true },
                "monitor:one": { "WallpaperId": "event-horizon", "Enabled": false }
              }
            }
            """);
        var restored = new AppSettingsStore(FilePath).Load();
        Assert.Equal(60, restored.FramesPerSecond);
        Assert.Single(restored.DisplayWallpapers);
        Assert.Equal("event-horizon", restored.DisplayWallpapers["MONITOR:ONE"].WallpaperId);
        Assert.False(restored.DisplayWallpapers["MONITOR:ONE"].Enabled);
    }

    [Fact]
    public void StoppingDisplayPersistsDisabledStateWithoutLosingItsWallpaperAndCustomization()
    {
        var store = new AppSettingsStore(FilePath);
        var settings = new AppSettings();
        settings.DisplayWallpapers["monitor:one"] = new()
        {
            WallpaperId = "living-fire", Enabled = true,
            Visualizers = new()
            {
                ["living-fire"] = new(ColorTheme: 1, Sparks: false),
                ["event-horizon"] = new(Scale: 1.8f)
            }
        };
        Assert.True(store.Save(settings));
        var active = store.Load();
        active.DisplayWallpapers["monitor:one"].Enabled = false;
        Assert.True(store.Save(active));

        var stopped = store.Load().DisplayWallpapers["monitor:one"];
        Assert.False(stopped.Enabled);
        Assert.Equal("living-fire", stopped.WallpaperId);
        Assert.Equal(2, stopped.Visualizers.Count);
        Assert.False(stopped.Visualizers["living-fire"].Sparks);
        Assert.Equal(1.8f, stopped.Visualizers["event-horizon"].Scale);
    }

    private void WriteJson(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
