namespace AnimatedWallPaper.Services;

internal sealed class WallpaperController : IDisposable
{
    private IWallpaperSession? _session;

    public bool IsRunning => _session is not null;

    public bool IsPaused { get; private set; }

    public int? ActiveProcessId => _session?.ProcessId;

    public void Start(int framesPerSecond, WallpaperKind kind, string? videoPath = null)
    {
        AppLog.Write($"WallpaperController.Start({framesPerSecond}). ExistingSession={_session is not null}");
        if (_session is not null) Stop();

        _session = kind switch
        {
            WallpaperKind.VisualizerDemo => new WallpaperSession(framesPerSecond, NativeRenderMode.VisualizerDemo),
            WallpaperKind.ExampleVideo when !string.IsNullOrWhiteSpace(videoPath) => new VideoWallpaperSession(videoPath),
            WallpaperKind.ExampleVideo => throw new InvalidOperationException("The example video path is missing."),
            _ => new WallpaperSession(framesPerSecond, NativeRenderMode.Ambient)
        };
        AppLog.Write("WallpaperSession construction returned");
        IsPaused = false;
    }

    public void Stop()
    {
        if (_session is null)
        {
            return;
        }

        var session = _session;
        _session = null;
        IsPaused = false;
        session.Dispose();
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
        _session?.SetFrameCap(framesPerSecond);
    }

    public void SetPausedMonitor(int? monitorIndex)
    {
        _session?.SetPausedMonitor(monitorIndex);
    }

    public void UpdateVisualizerSettings(VisualizerSettings settings)
    {
        _session?.UpdateVisualizerSettings(settings);
    }

    public void Dispose()
    {
        Stop();
    }
}
