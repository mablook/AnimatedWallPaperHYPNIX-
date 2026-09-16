using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class AssetContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryWallpaperDeclaresAuthorAndLicenseAndNonCommercialAssetsAreFlaggedForRemoval()
    {
        var wallpapersRoot = Path.Combine(RepositoryRoot, "Assets", "Wallpapers");

        // Licenses HYPNIX is allowed to ship in a commercial release.
        var commercialAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LicenseRef-HYPNIX-Proprietary", "CC0-1.0", "MIT", "Unlicense",
            "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause"
        };

        // Non-commercial assets are not shipped. HYPNIX is commercial, so this set must
        // stay empty; adding any non-commercially licensed wallpaper fails this gate.
        var expectedNonCommercial = Array.Empty<string>();

        var flaggedNonCommercial = new List<string>();
        foreach (var manifestPath in Directory.EnumerateFiles(wallpapersRoot, "wallpaper.json", SearchOption.AllDirectories))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;
            var id = root.GetProperty("id").GetString();
            var author = root.TryGetProperty("author", out var a) ? a.GetString() : null;
            var license = root.TryGetProperty("license", out var l) ? l.GetString() : null;
            var distribution = root.TryGetProperty("distribution", out var d) ? d.GetString() : null;

            Assert.False(string.IsNullOrWhiteSpace(author), $"{id}: manifest must declare a non-empty 'author'.");
            Assert.False(string.IsNullOrWhiteSpace(license), $"{id}: manifest must declare a non-empty 'license'.");

            if (!commercialAllowlist.Contains(license!))
            {
                // Anything not commercially licensed must be explicitly flagged so it
                // cannot be shipped silently in the commercial build.
                Assert.Equal("noncommercial", distribution);
                flaggedNonCommercial.Add(id!);
            }
        }

        Assert.Equal(
            expectedNonCommercial.OrderBy(x => x, StringComparer.Ordinal),
            flaggedNonCommercial.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("audio-visualizer-classic")]
    [InlineData("aethelis-audio-visualizer")]
    public void VisualizerPackageDeclaresExistingBackgroundAndPreview(string packageName)
    {
        var folder = Path.Combine(RepositoryRoot, "Assets", "Wallpapers", packageName);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "wallpaper.json")));
        var root = manifest.RootElement;

        Assert.Equal("native-audio-visualizer", root.GetProperty("type").GetString());
        Assert.Equal("wasapi-loopback", root.GetProperty("audio").GetString());
        Assert.Equal("independent-cover", root.GetProperty("multiMonitor").GetString());
        Assert.True(File.Exists(Path.Combine(folder, root.GetProperty("background").GetString()!)));
        Assert.True(File.Exists(Path.Combine(folder, root.GetProperty("preview").GetString()!)));
    }

    [Fact]
    public void TrayArtworkIsTransparentAndUsesMostOfTheCanvas()
    {
        using var bitmap = new Bitmap(Path.Combine(RepositoryRoot, "Assets", "Brand", "hypnix-tray-white.png"));
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        var opaquePixels = 0;

        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A <= 16) continue;
            opaquePixels++;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            Assert.True(pixel.R >= 235 && pixel.G >= 235 && pixel.B >= 235);
        }

        Assert.True(bitmap.GetPixel(0, 0).A == 0, "Tray icon corners must be transparent.");
        Assert.True(opaquePixels > bitmap.Width * bitmap.Height * 0.20, "Tray mark is too visually sparse.");
        Assert.True((maxX - minX + 1) >= bitmap.Width * 0.85, "Tray mark has excessive horizontal padding.");
        Assert.True((maxY - minY + 1) >= bitmap.Height * 0.75, "Tray mark has excessive vertical padding.");
    }

    [Fact]
    public void UiKeepsOnlyApprovedVisualizerAndExpectedIntensityCeiling()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot, "MainWindow.xaml"));
        // The per-wallpaper visualizer sliders live in the dedicated settings window.
        var settingsXaml = File.ReadAllText(Path.Combine(RepositoryRoot, "VisualizerSettingsWindow.xaml"));

        var catalog = WallpaperCatalog.LoadBuiltIns(Path.Combine(RepositoryRoot, "Assets", "Wallpapers"));
        Assert.Contains(catalog, entry => entry.Kind == WallpaperKind.VisualizerDemo);
        Assert.Contains(catalog, entry => entry.Kind == WallpaperKind.AethelisVisualizer);
        Assert.Contains(catalog, entry => entry.Kind == WallpaperKind.AethelisFlameBurst);
        Assert.DoesNotContain("Flame visualizer", xaml);
        Assert.DoesNotContain("Premium living flame", xaml);
        Assert.DoesNotContain("Aethelis GPU visualizer", xaml);
        Assert.Contains("Maximum=\"8\"", settingsXaml);   // intensity ceiling
        Assert.Contains("Maximum=\"12\"", settingsXaml);  // sensitivity ceiling
        Assert.Contains("Maximum=\"3\"", settingsXaml);   // glow ceiling
        Assert.Contains("Content=\"60 FPS\"", xaml);
    }

    [Fact]
    public void TrayUsesDedicatedTransparentIcon()
    {
        var code = File.ReadAllText(Path.Combine(RepositoryRoot, "MainWindow.xaml.cs"));

        Assert.Contains("hypnix-tray-white.ico", code);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, \"hypnix-v2.ico\")", code);
    }

    [Fact]
    public void ApprovedAethelisRemainsOneProceduralFireCircleWithoutParticles()
    {
        var shader = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", "Aethelis.hlsl"));
        var renderer = File.ReadAllText(Path.Combine(RepositoryRoot, "Services", "AethelisGpuRenderer.cs"));

        Assert.Contains("float flameHeight", shader);
        Assert.Contains("float3 fireColor", shader);
        Assert.DoesNotContain("for (int particle", shader);
        Assert.DoesNotContain("H-shaped", shader);
        Assert.Contains("string.Equals(shaderFileName, \"AethelisFlameBurst.hlsl\"", renderer);
        Assert.Contains("string.Equals(shaderFileName, \"FlamethrowerRingV2.hlsl\"", renderer);
        Assert.Contains("_usesFluidSimulation", renderer);
        Assert.Contains("CSSetUnorderedAccessView", renderer);
        Assert.Contains("_usesFireRingEffect", renderer);
    }

    [Fact]
    public void FlameBurstIsSeparateFromApprovedFireRingMilestone()
    {
        var approved = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", "Aethelis.hlsl"));
        var experimental = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", "AethelisFlameBurst.hlsl"));

        Assert.DoesNotContain("for (int fragment", approved);
        Assert.DoesNotContain("for (int flame", experimental);
        Assert.Contains("actual flames are rendered by Effekseer", experimental);
        Assert.DoesNotContain("for (int fragment", experimental);
    }

    [Fact]
    public void EffekseerBridgeAndIntegrationFixtureRemainPackagedForFutureEffects()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "NativeBin", "Hypnix.EffekseerBridge.dll")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "Assets", "Effects", "Aethelis", "Aura01_HDR.efkefc")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "Assets", "Effects", "HypnixFireRing", "HypnixFireRing.efkefc")));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "Assets", "Effects", "HypnixFireRing", "HypnixFireRingSmoke.efkefc")));

        var bridge = File.ReadAllText(Path.Combine(RepositoryRoot, "Native", "EffekseerBridge", "HypnixEffekseerBridge.cpp"));
        Assert.Contains("FlameCount = 48", bridge);
        Assert.Contains("SmokeCount = 12", bridge);
        Assert.Contains("SetDynamicInput", bridge);
        Assert.Contains("SetRotation", bridge);

        var renderer = File.ReadAllText(Path.Combine(RepositoryRoot, "Services", "AethelisGpuRenderer.cs"));
        Assert.Contains("_effekseerByViewport", renderer);
        Assert.Contains("viewportKey", renderer);

        var flamethrower = Path.Combine(RepositoryRoot, "Assets", "Effects", "FlamethrowerRingV2", "Runtime");
        Assert.True(File.Exists(Path.Combine(flamethrower, "HypnixFlamethrower.efkefc")));
        Assert.True(File.Exists(Path.Combine(flamethrower, "Texture", "hypnix_jet_pulse.png")));
    }

    [Fact]
    public void ApprovedFireRingV1MilestoneCannotBeSilentlyOverwritten()
    {
        var folder = Path.Combine(RepositoryRoot, "Assets", "Effects", "Milestones", "FireRingV1");
        Assert.True(File.Exists(Path.Combine(folder, "README.md")));
        Assert.Equal("5FCA0CA663074DE7F2DFFA364A68A734B36CA6205CBDE60B0D949C9656BFB437",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "HypnixFireRing.efkefc")))));
        Assert.Equal("685082792181F4149E86BCBA7F9A1E5714679FD32FF9709A415C6BC6B30E057E",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "HypnixFireRingSmoke.efkefc")))));
    }

    [Fact]
    public void VolumetricFireUsesPersistentGpuStateWithoutFlipbookDependencies()
    {
        var shader = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", "HypnixVolumetricFire.hlsl"));
        Assert.Contains("RWTexture3D<float4> NextVolume", shader);
        Assert.Contains("CSMain", shader);
        Assert.Contains("backtrace", shader);
        Assert.Contains("velocity.y -= density", shader);
        Assert.Contains("VolumeState.Load", shader);
        Assert.Contains("for(uint z=0;z<depth;z++)", shader);
        Assert.DoesNotContain("hypnix_jet_pulse", shader);
    }

    [Fact]
    public void RejectedVolumetricPrototypeStaysDocumentedAndHidden()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot, "MainWindow.xaml"));
        var research = Path.Combine(RepositoryRoot, "docs", "VOLUMETRIC_FIRE_RESEARCH.md");
        var target = Path.Combine(RepositoryRoot, "docs", "assets", "volumetric-fire-approved-target.png");

        var catalog = WallpaperCatalog.LoadBuiltIns(Path.Combine(RepositoryRoot, "Assets", "Wallpapers"));
        Assert.DoesNotContain(catalog, entry => entry.Kind == WallpaperKind.VolumetricFire);
        Assert.True(File.Exists(research));
        Assert.True(File.Exists(target));
        var findings = File.ReadAllText(research);
        Assert.Contains("Repeated failure patterns", findings);
        Assert.Contains("Required architecture for the next credible prototype", findings);
        Assert.Contains("Hide `Volumetric Fire 3D Prototype`", findings);
    }

    [Fact]
    public void EventHorizonDocumentsItsIndependentImplementationInTheCatalog()
    {
        var catalog = WallpaperCatalog.LoadBuiltIns(Path.Combine(RepositoryRoot, "Assets", "Wallpapers"));
        Assert.Contains(catalog, entry => entry.Kind == WallpaperKind.EventHorizon);
        var shader = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", "EventHorizon.hlsl"));
        Assert.Contains("float4 PSMain", shader);
        Assert.Contains("VSMain", shader);
        Assert.Contains("independent Schwarzschild", shader);
        Assert.DoesNotContain("shadertoy", shader.ToLowerInvariant());
    }

    [Theory]
    [InlineData("NeonRibbons")]
    [InlineData("LiquidOrbs")]
    [InlineData("EventHorizon")]
    public void RewrittenShadersDocumentTheirImplementationOrigin(string shaderName)
    {
        var shader = File.ReadAllText(Path.Combine(RepositoryRoot, "Shaders", shaderName + ".hlsl"));
        Assert.Contains(shaderName == "EventHorizon" ? "independent Schwarzschild" : "clean-room", shader);
        Assert.DoesNotContain("supplied by the user", shader);
    }

    [Fact]
    public void AudioReactiveToggleIsExposedInTheUi()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot, "MainWindow.xaml"));
        Assert.Contains("AudioReactiveCheckBox", xaml);
        Assert.Contains("AudioReactiveChanged", xaml);
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
