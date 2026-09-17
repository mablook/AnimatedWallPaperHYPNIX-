using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

// Standardized control contract: whatever the settings window shows must actually change the
// wallpaper. Palette and size/position are advertised through SupportsColorTheme and
// SupportsLayoutControls; these tests keep the capabilities aligned with the renderers.
public sealed class VisualizerCapabilityTests
{
    private static WallpaperEntry Entry(WallpaperKind kind) => new("id-" + kind, kind.ToString(), kind, null);

    // WallpaperKind is internal, so the [Theory] parameter is an int cast to keep the public
    // xUnit signature valid while still enumerating each concrete wallpaper kind.
    [Theory]
    // Every visualizer is recolored by the palette...
    [InlineData((int)WallpaperKind.VisualizerDemo, true)]
    [InlineData((int)WallpaperKind.AethelisVisualizer, true)]
    [InlineData((int)WallpaperKind.SpectralBloom, true)]
    [InlineData((int)WallpaperKind.NeonRibbons, true)]
    [InlineData((int)WallpaperKind.LiquidOrbs, true)]
    [InlineData((int)WallpaperKind.EventHorizon, true)]
    [InlineData((int)WallpaperKind.FractalPyramid, true)]
    [InlineData((int)WallpaperKind.Kaleidoscope, true)]
    [InlineData((int)WallpaperKind.Lotus, true)]
    [InlineData((int)WallpaperKind.LivingFire, true)]
    // ...except the two Effekseer fire effects, whose colors live in the authored effect.
    [InlineData((int)WallpaperKind.AethelisFlameBurst, false)]
    [InlineData((int)WallpaperKind.FlamethrowerRingV2, false)]
    // Non-visualizers expose no palette at all.
    [InlineData((int)WallpaperKind.BuiltIn, false)]
    [InlineData((int)WallpaperKind.ExampleVideo, false)]
    public void ColorThemeSupportMatchesRendererCapability(int kind, bool supported)
        => Assert.Equal(supported, Entry((WallpaperKind)kind).SupportsColorTheme);

    [Theory]
    // Size/position works on every visualizer whose renderer applies Scale/OffsetX/OffsetY.
    [InlineData((int)WallpaperKind.VisualizerDemo, true)]
    [InlineData((int)WallpaperKind.AethelisVisualizer, true)]
    [InlineData((int)WallpaperKind.SpectralBloom, true)]
    [InlineData((int)WallpaperKind.NeonRibbons, true)]
    [InlineData((int)WallpaperKind.LiquidOrbs, true)]
    [InlineData((int)WallpaperKind.EventHorizon, true)]
    [InlineData((int)WallpaperKind.FractalPyramid, true)]
    [InlineData((int)WallpaperKind.Kaleidoscope, true)]
    [InlineData((int)WallpaperKind.Lotus, true)]
    [InlineData((int)WallpaperKind.LivingFire, true)]
    // The Effekseer effects opt out until the authored effect exposes a transform.
    [InlineData((int)WallpaperKind.AethelisFlameBurst, false)]
    [InlineData((int)WallpaperKind.FlamethrowerRingV2, false)]
    [InlineData((int)WallpaperKind.BuiltIn, false)]
    [InlineData((int)WallpaperKind.ExampleVideo, false)]
    public void LayoutSupportMatchesRendererCapability(int kind, bool supported)
        => Assert.Equal(supported, Entry((WallpaperKind)kind).SupportsLayoutControls);

    [Fact]
    public void EffekseerEffectsHideBothPaletteAndLayout()
    {
        // The two authored fire effects keep only the controls that work (intensity/glow/audio).
        foreach (var kind in new[] { WallpaperKind.AethelisFlameBurst, WallpaperKind.FlamethrowerRingV2 })
        {
            var entry = Entry(kind);
            Assert.True(entry.IsVisualizer);
            Assert.False(entry.SupportsColorTheme);
            Assert.False(entry.SupportsLayoutControls);
        }
    }

    [Fact]
    public void FullScreenShaderWallpapersConsumeSizePositionAndPalette()
    {
        var root = FindRepositoryRoot();
        foreach (var shader in new[]
        {
            "Aethelis", "SpectralBloom", "NeonRibbons", "LiquidOrbs",
            "EventHorizon", "FractalPyramid", "Kaleidoscope", "Lotus"
        })
        {
            var code = File.ReadAllText(Path.Combine(root, "Shaders", shader + ".hlsl"));
            Assert.Contains("max(Scale", code);     // size/zoom is applied, not just declared
            Assert.Contains("OffsetX", code);        // horizontal position
            Assert.Contains("OffsetY", code);        // vertical position
            Assert.Contains("StartColor", code);     // palette is consumed
            Assert.Contains("EndColor", code);
        }
    }

    [Fact]
    public void GdiVisualizerAppliesTheSharedSizePositionTransform()
    {
        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Services", "NativeWallpaperHost.cs"));
        Assert.Contains("ApplyLayoutTransform", code);
        Assert.Contains("settings.OffsetX * unit, -settings.OffsetY * unit", code);
        Assert.Contains("graphics.ScaleTransform(scale, scale)", code);
    }

    [Fact]
    public void SettingsWindowHidesControlsThatHaveNoEffect()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "VisualizerSettingsWindow.xaml.cs"));
        Assert.Contains("ColorsCard.Visibility=Visible(entry.SupportsColorTheme)", code);
        Assert.Contains("LayoutControls.Visibility=Visible(entry.SupportsLayoutControls)", code);
        Assert.Contains("x:Name=\"ColorsCard\"", File.ReadAllText(Path.Combine(root, "VisualizerSettingsWindow.xaml")));
    }

    [Fact]
    public void AethelisDefaultsToWarmPaletteSoTintingKeepsTheApprovedLook()
    {
        var entries = WallpaperCatalog.LoadBuiltIns(Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers"));
        var aethelis = Assert.Single(entries.Where(e => e.Kind == WallpaperKind.AethelisVisualizer));
        Assert.Equal(1, aethelis.Defaults!.ColorTheme);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AnimatedWallPaper.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the HYPNIX repository root.");
    }
}
