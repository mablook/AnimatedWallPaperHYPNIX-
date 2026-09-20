namespace AnimatedWallPaper.Services;

internal sealed record WallpaperRequest(string Id, WallpaperKind Kind, int FramesPerSecond = 30,
    string? VideoPath = null, string? MediaToolsDirectory = null, string? BackgroundPath = null,
    VisualizerSettings? Settings = null, PreviewTarget? Preview = null,
    DesktopWorker.WallpaperTarget? Target = null);

internal sealed record PreviewTarget(IntPtr Parent, int Width, int Height);

internal static class WallpaperRenderGeometry
{
    internal static (int Width, int Height, DesktopWorker.WallpaperTarget[]? RenderTargets) Resolve(
        DesktopWorker.WallpaperTarget[]? renderTargets, PreviewTarget? preview,
        DesktopWorker.WallpaperTarget? target)
    {
        // A preview always fills its own child window, independently of the selected desktop.
        if (preview is not null) return (preview.Width, preview.Height, null);
        if (target is not { } monitor) return (1, 1, renderTargets);
        if (monitor.Width <= 0 || monitor.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(target), "A monitor must have positive pixel dimensions.");

        // DesktopWorker positions the host in desktop coordinates. Inside that window the
        // selected display is viewport zero, with a local origin, even on mixed-DPI desktops.
        return (monitor.Width, monitor.Height, [monitor with { X = 0, Y = 0 }]);
    }
}

internal static class WallpaperSessionFactory
{
    public static Task<IWallpaperSession> CreateAsync(WallpaperRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return request.Kind == WallpaperKind.ExampleVideo
            ? VideoWallpaperSession.CreateAsync(request, token)
            : Task.FromResult<IWallpaperSession>(new WallpaperSession(request));
    }

    public static NativeRenderMode RenderMode(WallpaperKind kind) => kind switch
    {
        WallpaperKind.VisualizerDemo => NativeRenderMode.VisualizerDemo,
        WallpaperKind.AethelisVisualizer => NativeRenderMode.AethelisVisualizer,
        WallpaperKind.AethelisFlameBurst => NativeRenderMode.AethelisFlameBurst,
        WallpaperKind.FlamethrowerRingV2 => NativeRenderMode.FlamethrowerRingV2,
        WallpaperKind.SpectralBloom => NativeRenderMode.SpectralBloom,
        WallpaperKind.NeonRibbons => NativeRenderMode.NeonRibbons,
        WallpaperKind.LiquidOrbs => NativeRenderMode.LiquidOrbs,
        WallpaperKind.EventHorizon => NativeRenderMode.EventHorizon,
        WallpaperKind.FractalPyramid => NativeRenderMode.FractalPyramid,
        WallpaperKind.Kaleidoscope => NativeRenderMode.Kaleidoscope,
        WallpaperKind.Lotus => NativeRenderMode.Lotus,
        WallpaperKind.LivingFire => NativeRenderMode.LivingFire,
        WallpaperKind.VolumetricFire => NativeRenderMode.VolumetricFire,
        WallpaperKind.ExampleVideo => NativeRenderMode.Video,
        _ => NativeRenderMode.Ambient
    };
}
