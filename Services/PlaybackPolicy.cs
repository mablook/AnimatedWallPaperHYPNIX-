namespace AnimatedWallPaper.Services;

internal readonly record struct PlaybackDecision(bool PauseAll, IReadOnlyList<int> PausedMonitors, string Reason);

internal static class PlaybackPolicy
{
    // appMode: 0 = never, 1 = fullscreen apps only, 2 = fullscreen or windowed apps.
    // fullscreenMonitors: indices whose monitor is fully covered by a foreign app window (borderless/fullscreen).
    // visibleAppMonitors:    indices with any visible, non-minimized foreign app window.
    // perMonitor:         "Active display only" scope. Only the covered monitors freeze; clean monitors keep running.
    //                     When false ("All displays"), a single covered monitor freezes the whole desktop.
    public static PlaybackDecision Evaluate(
        int appMode,
        IReadOnlyCollection<int> fullscreenMonitors,
        IReadOnlyCollection<int> visibleAppMonitors,
        bool pauseOnBattery, bool onBattery,
        bool perMonitor,
        bool sessionLocked = false)
    {
        if (sessionLocked) return new(true, Array.Empty<int>(), "session locked");
        if (pauseOnBattery && onBattery) return new(true, Array.Empty<int>(), "battery power");

        var occupied = appMode switch
        {
            1 => fullscreenMonitors,
            2 => visibleAppMonitors,
            _ => Array.Empty<int>()
        };

        var monitors = occupied.Where(index => index >= 0).Distinct().OrderBy(index => index).ToArray();
        if (monitors.Length == 0) return new(false, Array.Empty<int>(), "ready");

        if (perMonitor)
        {
            var reason = monitors.Length == 1
                ? $"display {monitors[0] + 1} paused"
                : $"{monitors.Length} displays paused";
            return new(false, monitors, reason);
        }

        return new(true, Array.Empty<int>(), appMode == 1 ? "fullscreen app detected" : "visible app window detected");
    }
}
