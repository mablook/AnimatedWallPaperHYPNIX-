namespace AnimatedWallPaper.Services;

internal readonly record struct PlaybackDecision(bool PauseAll, int? PausedMonitor, string Reason);

internal static class PlaybackPolicy
{
    public static PlaybackDecision Evaluate(int appMode, bool fullscreen, bool otherApp,
        bool pauseOnBattery, bool onBattery, bool perMonitor, int? foregroundMonitor,
        bool sessionLocked = false)
    {
        if (sessionLocked) return new(true, null, "session locked");
        if (pauseOnBattery && onBattery) return new(true, null, "battery power");
        var pauseForApp = appMode == 1 && fullscreen || appMode == 2 && otherApp;
        if (!pauseForApp) return new(false, null, "ready");
        if (perMonitor && foregroundMonitor is >= 0)
            return new(false, foregroundMonitor, $"display {foregroundMonitor.Value + 1} paused");
        return new(true, null, fullscreen ? "fullscreen app detected" : "another app is active");
    }
}
