namespace AnimatedWallPaper.Services;

internal sealed record WallpaperRequest(string Id, WallpaperKind Kind, int FramesPerSecond = 30,
    string? VideoPath = null, string? MediaToolsDirectory = null, string? BackgroundPath = null,
    VisualizerSettings? Settings = null, PreviewTarget? Preview = null);

internal sealed record PreviewTarget(IntPtr Parent, int Width, int Height);

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
