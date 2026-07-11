using System.Drawing;
using System.Text.Json;

namespace Hypnix.Tests;

public sealed class AssetContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("audio-visualizer-classic")]
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

        Assert.Contains("Visualizer demo", xaml);
        Assert.DoesNotContain("Flame visualizer", xaml);
        Assert.DoesNotContain("Premium living flame", xaml);
        Assert.DoesNotContain("Aethelis GPU visualizer", xaml);
        Assert.Contains("Maximum=\"2.7\"", xaml);
        Assert.Contains("Content=\"60 FPS\"", xaml);
    }

    [Fact]
    public void TrayUsesDedicatedTransparentIcon()
    {
        var code = File.ReadAllText(Path.Combine(RepositoryRoot, "MainWindow.xaml.cs"));

        Assert.Contains("hypnix-tray-white.ico", code);
        Assert.DoesNotContain("Path.Combine(AppContext.BaseDirectory, \"hypnix-v2.ico\")", code);
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
