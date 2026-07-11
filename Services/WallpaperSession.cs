namespace AnimatedWallPaper.Services;

internal sealed class WallpaperSession : IWallpaperSession
{
    private readonly NativeWallpaperHost _host;
    private readonly AudioSpectrumService? _audioSpectrum;
    public int? ProcessId => null;

    public WallpaperSession(int framesPerSecond, NativeRenderMode renderMode, DesktopWorker.WallpaperTarget? target = null)
    {
        try
        {
            _host = new NativeWallpaperHost(renderMode, DesktopWorker.GetMonitorTargets());
            if (renderMode == NativeRenderMode.VisualizerDemo)
            {
                _audioSpectrum = new AudioSpectrumService();
                _audioSpectrum.BandsAvailable += _host.SubmitAudioBands;
                _audioSpectrum.Start();
            }
            DesktopWorker.AttachWallpaperWindow(_host.Handle, target);
            _host.Start(framesPerSecond);
            AppLog.Write($"WallpaperSession started with native renderer. Host=0x{_host.Handle.ToInt64():X}");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void SetFrameCap(int framesPerSecond) => _host.SetFrameCap(framesPerSecond);

    public void Resume() => _host.Resume();

    public void Pause() => _host.Pause();

    public void SetPausedMonitor(int? monitorIndex) => _host.SetPausedMonitor(monitorIndex);

    public void UpdateVisualizerSettings(VisualizerSettings settings) => _host.UpdateVisualizerSettings(settings);

    public void Dispose()
    {
        _audioSpectrum?.Dispose();
        _host?.Dispose();
    }
}
