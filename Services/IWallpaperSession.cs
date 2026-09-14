namespace AnimatedWallPaper.Services;

internal interface IWallpaperSession : IDisposable
{
    int? ProcessId { get; }
    bool IsHealthy { get; }
    void Show();
    void SetFrameCap(int framesPerSecond);
    void Resume();
    void Pause();
    void SetPausedMonitor(int? monitorIndex);
    void UpdateVisualizerSettings(VisualizerSettings settings);
}
