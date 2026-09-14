namespace AnimatedWallPaper.Services;

internal interface IWallpaperSession : IDisposable
{
    int? ProcessId { get; }
    bool IsHealthy { get; }
    void Show();
    void SetFrameCap(int framesPerSecond);
    void Resume();
    void Pause();
    void SetPausedMonitors(IReadOnlyList<int> monitorIndices);
    void UpdateVisualizerSettings(VisualizerSettings settings);
}
