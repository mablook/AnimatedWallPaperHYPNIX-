using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class LifecycleTests
{
    private static WallpaperRequest Request(string id) => new(id, WallpaperKind.BuiltIn);

    [Fact]
    public async Task FailedPreparationKeepsCurrentWallpaperAndPauseState()
    {
        var current = new FakeSession();
        using var controller = new WallpaperController((request, _) => request.Id == "current"
            ? Task.FromResult<IWallpaperSession>(current) : Task.FromException<IWallpaperSession>(new IOException("missing video")));
        await controller.StartAsync(Request("current"));
        controller.Pause();
        await Assert.ThrowsAsync<IOException>(() => controller.StartAsync(Request("missing")));
        Assert.True(controller.IsRunning);
        Assert.True(controller.IsPaused);
        Assert.False(current.Disposed);
        Assert.Equal("current", controller.ActiveRequest!.Id);
    }

    [Fact]
    public async Task ReplacementShowsNewFrameBeforeDisposingPreviousSession()
    {
        var previous = new FakeSession();
        var next = new FakeSession { OnShow = () => Assert.False(previous.Disposed) };
        using var controller = new WallpaperController((request, _) => Task.FromResult<IWallpaperSession>(request.Id == "old" ? previous : next));
        await controller.StartAsync(Request("old"));
        await controller.StartAsync(Request("new"));
        Assert.True(previous.Disposed);
        Assert.True(next.Shown);
        Assert.Equal("new", controller.ActiveRequest!.Id);
    }

    [Fact]
    public async Task FailedShowDisposesCandidateAndPreservesPreviousSession()
    {
        var previous = new FakeSession();
        var next = new FakeSession { OnShow = () => throw new IOException("device removed") };
        using var controller = new WallpaperController((request, _) => Task.FromResult<IWallpaperSession>(request.Id == "old" ? previous : next));
        await controller.StartAsync(Request("old"));
        await Assert.ThrowsAsync<IOException>(() => controller.StartAsync(Request("new")));
        Assert.False(previous.Disposed);
        Assert.True(next.Disposed);
        Assert.Equal("old", controller.ActiveRequest!.Id);
    }

    [Fact]
    public async Task StopWhilePreparingCannotResurrectWallpaper()
    {
        var ready = new TaskCompletionSource<IWallpaperSession>();
        var pendingSession = new FakeSession();
        using var controller = new WallpaperController((_, _) => ready.Task);
        var start = controller.StartAsync(Request("late"));
        controller.Stop();
        ready.SetResult(pendingSession);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(controller.IsRunning);
        Assert.False(pendingSession.Shown);
        Assert.True(pendingSession.Disposed);
    }

    [Fact]
    public async Task LateOlderSelectionCannotReplaceNewerSelection()
    {
        var ready = new TaskCompletionSource<IWallpaperSession>();
        var stale = new FakeSession();
        var newest = new FakeSession();
        using var controller = new WallpaperController((request, _) => request.Id == "slow" ? ready.Task : Task.FromResult<IWallpaperSession>(newest));
        var first = controller.StartAsync(Request("slow"));
        await controller.StartAsync(Request("latest"));
        ready.SetResult(stale);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal("latest", controller.ActiveRequest!.Id);
        Assert.True(stale.Disposed);
        Assert.False(newest.Disposed);
    }

    [Fact]
    public async Task PausedVideoReaderWaitsUntilResumeAndCanBeCancelled()
    {
        var gate = new AsyncPauseGate();
        gate.Pause();
        var blocked = gate.WaitAsync(CancellationToken.None);
        Assert.False(blocked.IsCompleted);
        gate.Resume();
        await blocked.WaitAsync(TimeSpan.FromSeconds(1));
        gate.Pause();
        using var cancel = new CancellationTokenSource();
        var cancelled = gate.WaitAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }

    private sealed class FakeSession : IWallpaperSession
    {
        public bool Disposed { get; private set; }
        public bool Shown { get; private set; }
        public Action? OnShow { get; init; }
        public bool IsHealthy => !Disposed;
        public int? ProcessId => null;
        public void Show() { OnShow?.Invoke(); Shown = true; }
        public void Dispose() => Disposed = true;
        public void Pause() { }
        public void Resume() { }
        public void SetPausedMonitor(int? monitorIndex) { }
        public void SetFrameCap(int framesPerSecond) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
    }
}
