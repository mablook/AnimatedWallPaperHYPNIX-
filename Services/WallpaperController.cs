namespace AnimatedWallPaper.Services;

internal sealed class WallpaperController : IDisposable
{
    private IWallpaperSession? _session;
    private readonly Func<WallpaperRequest, CancellationToken, Task<IWallpaperSession>> _factory;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private bool _audioEnabled = true;
    public WallpaperRequest? ActiveRequest { get; private set; }
    public bool IsHealthy => _session?.IsHealthy ?? false;
    public event Action? StateChanged;

    public WallpaperController(Func<WallpaperRequest, CancellationToken, Task<IWallpaperSession>>? factory = null)
        => _factory = factory ?? WallpaperSessionFactory.CreateAsync;

    public bool IsRunning => _session is not null;

    public bool IsPaused { get; private set; }

    public int? ActiveProcessId => _session?.ProcessId;

    public async Task StartAsync(WallpaperRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pending?.Cancel();
        using var pending = new CancellationTokenSource();
        _pending = pending;
        IWallpaperSession? next = null;
        try
        {
            next = await _factory(request, pending.Token);
            pending.Token.ThrowIfCancellationRequested();
            if (request.Settings is not null) next.UpdateVisualizerSettings(request.Settings);
            next.SetAudioEnabled(_audioEnabled);
            next.Show();
            var previous = _session;
            _session = next;
            next = null;
            ActiveRequest = request;
            IsPaused = false;
            try { previous?.Dispose(); }
            catch (Exception exception) { AppLog.WriteException("Previous wallpaper cleanup failed", exception); }
            StateChanged?.Invoke();
        }
        finally
        {
            if (ReferenceEquals(_pending, pending)) _pending = null;
            next?.Dispose();
        }
    }

    public void Stop()
    {
        _pending?.Cancel();
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        ActiveRequest = null;
        IsPaused = false;
        session.Dispose();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (_session is null || IsPaused)
        {
            return;
        }

        _session.Pause();
        IsPaused = true;
        AppLog.Write("Wallpaper paused");
    }

    public void Resume()
    {
        if (_session is null || !IsPaused)
        {
            return;
        }

        _session.Resume();
        IsPaused = false;
        AppLog.Write("Wallpaper resumed");
    }

    public void SetFrameCap(int framesPerSecond)
    {
        if (ActiveRequest is not null) ActiveRequest = ActiveRequest with { FramesPerSecond = framesPerSecond };
        _session?.SetFrameCap(framesPerSecond);
    }

    public void SetPausedMonitors(IReadOnlyList<int> monitorIndices)
    {
        _session?.SetPausedMonitors(monitorIndices);
    }

    public void UpdateVisualizerSettings(VisualizerSettings settings)
    {
        if (ActiveRequest is not null) ActiveRequest = ActiveRequest with { Settings = settings };
        _session?.UpdateVisualizerSettings(settings);
    }

    // Global audio-capture toggle. When disabled the active and future sessions stop
    // consuming the WASAPI stream; playing only the animation ("relax" mode).
    public bool AudioEnabled => _audioEnabled;
    public void SetAudioEnabled(bool enabled)
    {
        _audioEnabled = enabled;
        _session?.SetAudioEnabled(enabled);
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
