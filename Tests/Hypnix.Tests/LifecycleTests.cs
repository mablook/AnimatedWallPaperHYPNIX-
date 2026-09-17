using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class LifecycleTests
{
    private static WallpaperRequest Request(string id) => new(id, WallpaperKind.BuiltIn);

    [Fact]
    public async Task FirstFrameTimeoutKeepsPreviousWallpaperEvenAfterLateCompletion()
    {
        using var release = new ManualResetEventSlim();
        using var worker = new WallpaperRenderWorker(() => release.Wait(), () => { }, () => 1, () => { });
        var previous = new FakeSession();
        var candidate = new FakeSession();
        using var controller = new WallpaperController((request, _) =>
        {
            if (request.Id == "old") return Task.FromResult<IWallpaperSession>(previous);
            worker.Start(TimeSpan.FromMilliseconds(50));
            return Task.FromResult<IWallpaperSession>(candidate);
        });
        try
        {
            await controller.StartAsync(Request("old"));
            controller.Pause();
            await Assert.ThrowsAsync<TimeoutException>(() => controller.StartAsync(Request("slow")));
            Assert.False(previous.Disposed);
            Assert.True(controller.IsPaused);
        }
        finally { release.Set(); }
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("old", controller.ActiveRequest!.Id);
        Assert.False(previous.Disposed);
        Assert.False(candidate.Shown);
    }

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

    [Fact]
    public async Task StartupAppliesTheDefaultAudioEnabledStateToNewSession()
    {
        var session = new FakeSession();
        using var controller = new WallpaperController((_, _) => Task.FromResult<IWallpaperSession>(session));
        await controller.StartAsync(Request("a"));
        Assert.True(session.AudioEnabled == true);
        Assert.True(controller.AudioEnabled);
    }

    [Fact]
    public async Task DisablingAudioForwardsToActiveSessionAndPersistsToNextSession()
    {
        var first = new FakeSession();
        var second = new FakeSession();
        using var controller = new WallpaperController((request, _) =>
            Task.FromResult<IWallpaperSession>(request.Id == "first" ? first : second));
        await controller.StartAsync(Request("first"));
        controller.SetAudioEnabled(false);
        Assert.True(first.AudioEnabled == false);
        Assert.False(controller.AudioEnabled);
        await controller.StartAsync(Request("second"));
        Assert.True(second.AudioEnabled == false); // new session inherits the off state
    }

    [Fact]
    public async Task AudioDisabledBeforeStartAppliesToTheFirstSession()
    {
        var session = new FakeSession();
        using var controller = new WallpaperController((_, _) => Task.FromResult<IWallpaperSession>(session));
        controller.SetAudioEnabled(false);
        await controller.StartAsync(Request("x"));
        Assert.True(session.AudioEnabled == false);
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
        public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) { }
        public void SetFrameCap(int framesPerSecond) { }
        public void UpdateVisualizerSettings(VisualizerSettings settings) { }
        public bool? AudioEnabled { get; private set; }
        public void SetAudioEnabled(bool enabled) => AudioEnabled = enabled;
    }
}
