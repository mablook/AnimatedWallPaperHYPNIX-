namespace AnimatedWallPaper.Services;

internal sealed class WallpaperSession : IWallpaperSession
{
    private readonly NativeWallpaperHost _host;
    private IDisposable? _audioSubscription;
    private readonly bool _usesAudio;
    public int? ProcessId => null;
    public bool IsHealthy => _host.IsHealthy;

    public WallpaperSession(WallpaperRequest request)
    {
        try
        {
            var mode = WallpaperSessionFactory.RenderMode(request.Kind);
            _host = new NativeWallpaperHost(mode,
                request.Preview is null ? DesktopWorker.GetMonitorTargets() : null,
                mode == NativeRenderMode.VisualizerDemo ? request.BackgroundPath : null, request.Preview);
            _usesAudio = mode != NativeRenderMode.Ambient;
            if (_usesAudio) _audioSubscription = AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
            if (request.Preview is null)
                DesktopWorker.AttachWallpaperWindow(_host.Handle,
                    useLayeredWindow: mode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire));
            _host.Start(request.FramesPerSecond, reveal: false);
        }
        catch { Dispose(); throw; }
    }

    public void Show() => _host.Show();
    public void SetFrameCap(int framesPerSecond) => _host.SetFrameCap(framesPerSecond);
    public void Resume()
    {
        if (_usesAudio) _audioSubscription ??= AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
        _host.Resume();
    }
    public void Pause()
    {
        _host.Pause();
        _audioSubscription?.Dispose();
        _audioSubscription = null;
    }
    public void SetPausedMonitor(int? monitorIndex) => _host.SetPausedMonitor(monitorIndex);
    public void UpdateVisualizerSettings(VisualizerSettings settings) => _host.UpdateVisualizerSettings(settings);
    public void Dispose()
    {
        _audioSubscription?.Dispose();
        _audioSubscription = null;
        _host?.Dispose();
    }
}
