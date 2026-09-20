using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class MonitorRenderTargetTests
{
    [Theory]
    [InlineData(3840, 1080, 1920, 1080, 96)]
    [InlineData(0, 0, 3840, 2160, 144)]
    [InlineData(5760, 0, 1080, 1920, 120)]
    [InlineData(-1920, -600, 1920, 1200, 96)]
    public void TargetedHostUsesOneLocalViewportAndNativeMonitorPixelSize(
        int x, int y, int width, int height, uint dpi)
    {
        var target = new DesktopWorker.WallpaperTarget(x, y, width, height,
            "monitor-stable-id", @"\\.\DISPLAY3", dpi, dpi);
        var geometry = WallpaperRenderGeometry.Resolve(null, null, target);

        Assert.Equal(width, geometry.Width);
        Assert.Equal(height, geometry.Height);
        var viewport = Assert.Single(geometry.RenderTargets!);
        Assert.Equal(0, viewport.X);
        Assert.Equal(0, viewport.Y);
        Assert.Equal(width, viewport.Width);
        Assert.Equal(height, viewport.Height);
        Assert.Equal(target.DeviceId, viewport.DeviceId);
        Assert.Equal(target.DeviceName, viewport.DeviceName);
        Assert.Equal(dpi, viewport.DpiX);
        Assert.Equal(dpi, viewport.DpiY);
        // Rebasing the render viewport must never change where DesktopWorker places the host.
        Assert.Equal(x, target.X);
        Assert.Equal(y, target.Y);
    }

    [Fact]
    public void TargetingSecondDisplayDoesNotRenderFirstDisplayOrApplyDesktopOffsetTwice()
    {
        DesktopWorker.WallpaperTarget[] monitors =
        [
            new(0, 600, 3840, 2160, "first"),
            new(3840, 0, 1080, 1920, "portrait")
        ];

        var geometry = WallpaperRenderGeometry.Resolve(monitors, null, monitors[1]);

        var viewport = Assert.Single(geometry.RenderTargets!);
        Assert.Equal("portrait", viewport.DeviceId);
        Assert.Equal((0, 0, 1080, 1920), (viewport.X, viewport.Y, viewport.Width, viewport.Height));
        Assert.Equal((1080, 1920), (geometry.Width, geometry.Height));
        Assert.Equal(3840, monitors[1].X);
    }

    [Fact]
    public void LegacyDesktopRequestKeepsEveryViewportInDesktopCoordinates()
    {
        DesktopWorker.WallpaperTarget[] monitors =
        [
            new(0, 1080, 2560, 1440, "first"),
            new(2560, 0, 3840, 2160, "second")
        ];

        var geometry = WallpaperRenderGeometry.Resolve(monitors, null, null);

        Assert.Equal(monitors, geometry.RenderTargets);
        Assert.Equal(1080, geometry.RenderTargets![0].Y);
        Assert.Equal(2560, geometry.RenderTargets[1].X);
    }

    [Fact]
    public void PreviewUsesItsOwnDimensionsAndIgnoresDesktopSelection()
    {
        DesktopWorker.WallpaperTarget[] monitors = [new(0, 0, 3840, 2160)];
        var preview = new PreviewTarget(new IntPtr(42), 420, 236);
        var geometry = WallpaperRenderGeometry.Resolve(monitors, preview,
            new DesktopWorker.WallpaperTarget(3840, 1000, 1080, 1920, "portrait", DpiX: 144, DpiY: 144));

        Assert.Equal((420, 236), (geometry.Width, geometry.Height));
        Assert.Null(geometry.RenderTargets);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void InvalidTargetCannotCreateAnEmptyOrNegativeSizedDesktopHost(int width, int height)
    {
        var target = new DesktopWorker.WallpaperTarget(0, 0, width, height);
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperRenderGeometry.Resolve(null, null, target));
    }

    [Fact]
    public void PreviewDoesNotFailBecauseItsPreviouslySelectedDesktopDisconnected()
    {
        var preview = new PreviewTarget(new IntPtr(42), 480, 270);
        var geometry = WallpaperRenderGeometry.Resolve(null, preview, default(DesktopWorker.WallpaperTarget));
        Assert.Equal((480, 270), (geometry.Width, geometry.Height));
        Assert.Null(geometry.RenderTargets);
    }
}
