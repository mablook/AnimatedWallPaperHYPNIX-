using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class WallpaperPackageValidatorTests
{
    [Fact]
    public void ValidKnownNativePresetIsReady()
    {
        using var package = TestPackage.Create("safe-preset", "hypnix.visualizer.classic.v1");

        var result = WallpaperPackageValidator.Validate(package.DirectoryPath);

        Assert.Equal(WallpaperPackageStatus.Ready, result.Status);
        Assert.Null(result.Error);
    }

    [Fact]
    public void FutureRendererIsRecognizedWithoutBeingExecuted()
    {
        using var package = TestPackage.Create("future-flame", "hypnix.flame-fluid.v1");

        var result = WallpaperPackageValidator.Validate(package.DirectoryPath);

        Assert.Equal(WallpaperPackageStatus.UnsupportedRenderer, result.Status);
        Assert.Contains("not installed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathTraversalIsRejected()
    {
        using var package = TestPackage.Create("traversal", "hypnix.visualizer.classic.v1",
            entrypoint: "../outside.json");

        var result = WallpaperPackageValidator.Validate(package.DirectoryPath);

        Assert.Equal(WallpaperPackageStatus.Invalid, result.Status);
        Assert.Contains("escapes", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutableContentIsRejectedEvenWithAValidManifest()
    {
        using var package = TestPackage.Create("contains-exe", "hypnix.visualizer.classic.v1");
        File.WriteAllBytes(Path.Combine(package.DirectoryPath, "content", "payload.exe"), [0x4D, 0x5A]);

        var result = WallpaperPackageValidator.Validate(package.DirectoryPath);

        Assert.Equal(WallpaperPackageStatus.Invalid, result.Status);
        Assert.Contains("forbidden", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownRendererIsRejected()
    {
        using var package = TestPackage.Create("unknown-renderer", "third-party.execute-anything");

        var result = WallpaperPackageValidator.Validate(package.DirectoryPath);

        Assert.Equal(WallpaperPackageStatus.Invalid, result.Status);
        Assert.Contains("not recognized", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestPackage : IDisposable
    {
        private TestPackage(string root, string directoryPath)
        {
            Root = root;
            DirectoryPath = directoryPath;
        }

        private string Root { get; }
        public string DirectoryPath { get; }

        public static TestPackage Create(string id, string rendererId, string entrypoint = "content/preset.json")
        {
            var root = Path.Combine(Path.GetTempPath(), "hypnix-tests", Guid.NewGuid().ToString("N"));
            var package = Path.Combine(root, id);
            Directory.CreateDirectory(Path.Combine(package, "content"));
            File.WriteAllText(Path.Combine(package, "preview.png"), "preview");
            File.WriteAllText(Path.Combine(package, "background.png"), "background");
            File.WriteAllText(Path.Combine(package, "content", "preset.json"), "{}");
            File.WriteAllText(Path.Combine(package, "wallpaper.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "title": "Test package",
              "type": "native-preset",
              "rendererId": "{{rendererId}}",
              "entrypoint": "{{entrypoint}}",
              "preview": "preview.png",
              "background": "background.png"
            }
            """);
            return new TestPackage(root, package);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
