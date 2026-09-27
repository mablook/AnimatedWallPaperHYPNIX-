namespace AnimatedWallPaper.Services;

internal sealed class WallpaperSession : IWallpaperSession
{
    private readonly NativeWallpaperHost _host;
    private readonly int _framesPerSecond;
    private IDisposable? _audioSubscription;
    private readonly bool _usesAudio;
    private bool _audioEnabled = true;
    private bool _paused;
    public int? ProcessId => null;
    public bool IsHealthy => _host.IsHealthy;

    // Creates the host, subscribes audio and attaches to the desktop. These are quick, UI-thread-affine
    // steps (CreateWindowEx, SetParent). The heavy first-frame preparation (GPU device, shader compile)
    // is started separately by StartAsync/CreateAsync so it does not block the caller. Constructing a
    // session on its own leaves it ready-but-not-rendering; production always uses CreateAsync.
    public WallpaperSession(WallpaperRequest request)
    {
        try
        {
            var mode = WallpaperSessionFactory.RenderMode(request.Kind);
            _framesPerSecond = request.FramesPerSecond;
            _host = new NativeWallpaperHost(mode,
                request.Preview is null && request.Target is null ? DesktopWorker.GetMonitorTargets() : null,
                mode == NativeRenderMode.VisualizerDemo ? request.BackgroundPath : null, request.Preview, request.Target);
            _usesAudio = mode != NativeRenderMode.Ambient;
            if (_usesAudio) _audioSubscription = AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
            if (request.Preview is null)
                DesktopWorker.AttachWallpaperWindow(_host.Handle, request.Target,
                    useLayeredWindow: mode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon or NativeRenderMode.FractalPyramid or NativeRenderMode.Kaleidoscope or NativeRenderMode.Lotus or NativeRenderMode.LivingFire));
            _host.UpdateVisualizerSettings(request.Settings??VisualizerSettings.Default);
        }
        catch { Dispose(); throw; }
    }

    // Builds a session and awaits its first frame without blocking the caller's thread, so applying a
    // wallpaper never freezes the UI. On any preparation fault (including the first-frame timeout) the
    // partially built session is disposed and the fault is surfaced so the controller keeps the previous
    // wallpaper, exactly as before.
    public static async Task<IWallpaperSession> CreateAsync(WallpaperRequest request, CancellationToken token)
    {
        var session = new WallpaperSession(request);
        try
        {
            await session._host.StartAsync(session._framesPerSecond, reveal: false, token);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public void Show() => _host.Show();
    public void SetFrameCap(int framesPerSecond) => _host.SetFrameCap(framesPerSecond);
    public void Resume()
    {
        _paused = false;
        if (_usesAudio && _audioEnabled) _audioSubscription ??= AudioSpectrumSource.Subscribe(_host.SubmitAudioBands);
        _host.Resume();
    }
    public void Pause()
    {
        _paused = true;
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
        if (enabled && !_paused)
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
