namespace AnimatedWallPaper.Services;

internal sealed class WallpaperSession : IWallpaperSession
{
    private readonly NativeWallpaperHost _host;
    private IDisposable? _audioSubscription;
    private readonly bool _usesAudio;
    private bool _audioEnabled = true;
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
                    useLayeredWindow: mode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon));
            _host.Start(request.FramesPerSecond, reveal: false);
        }
        catch { Dispose(); throw; }
    }

    public void Show() => _host.Show();
    public void SetFrameCap(int framesPerSecond) => _host.SetFrameCap(framesPerSecond);
    public void Resume()
    {
        if (_usesAudio && _audioEnabled) _audioSubscription ??= AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
        _host.Resume();
    }
    public void Pause()
    {
        _host.Pause();
        _audioSubscription?.Dispose();
        _audioSubscription = null;
    }
    public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) => _host.SetPausedMonitors(monitorIndices);
    public void UpdateVisualizerSettings(VisualizerSettings settings) => _host.UpdateVisualizerSettings(settings);
    public void SetAudioEnabled(bool enabled)
    {
        if (_audioEnabled == enabled) return;
        _audioEnabled = enabled;
        if (!_usesAudio) return;
        if (enabled)
        {
            _audioSubscription ??= AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
        }
        else
        {
            _audioSubscription?.Dispose();
            _audioSubscription = null;
            _host.SubmitAudioBands(new float[64]); // settle to a calm, non-reactive state
        }
    }
    public void Dispose()
    {
        _audioSubscription?.Dispose();
        _audioSubscription = null;
        _host?.Dispose();
    }
}
