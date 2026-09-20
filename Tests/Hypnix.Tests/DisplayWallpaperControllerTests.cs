using AnimatedWallPaper.Services;
using Target = AnimatedWallPaper.Services.DesktopWorker.WallpaperTarget;

namespace Hypnix.Tests;

public sealed class DisplayWallpaperControllerTests
{
    private static readonly Target Left = new(0, 0, 1920, 1080, "monitor-left", "DISPLAY1");
    private static readonly Target Right = new(1920, 0, 2560, 1440, "monitor-right", "DISPLAY2", 144, 144);
    private static WallpaperRequest Request(string id, VisualizerSettings? settings = null)
        => new(id, WallpaperKind.BuiltIn, Settings: settings);
    private static TaskCompletionSource<IWallpaperSession> Pending()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ApplyingAndReplacingOneDisplayPreservesOtherDisplay()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("fire"), Left);
        await controller.StartAsync(Request("orbs"), Right);
        var snapshots = controller.ActiveRequests;
        await controller.StartAsync(Request("lotus"), Left);
        Assert.Equal("lotus", controller.ActiveRequests[Left.DeviceId].Id);
        Assert.Equal("orbs", controller.ActiveRequests[Right.DeviceId].Id);
        Assert.Equal(Right, controller.ActiveRequests[Right.DeviceId].Target);
        Assert.Equal("fire", snapshots[Left.DeviceId].Id);
        Assert.True(made[0].Disposed);
        Assert.False(made[1].Disposed);
        Assert.Equal(2, controller.DesiredRequests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationOrRevealFailurePreservesOldAssignmentsAndPause(bool failDuringShow)
    {
        var current = new FakeSession();
        var other = new FakeSession();
        var failed = new FakeSession { OnShow = () => throw new IOException("GPU removed") };
        using var controller = new DisplayWallpaperController((request, _) => request.Id switch
        {
            "current" => Task.FromResult<IWallpaperSession>(current),
            "other" => Task.FromResult<IWallpaperSession>(other),
            _ when failDuringShow => Task.FromResult<IWallpaperSession>(failed),
            _ => Task.FromException<IWallpaperSession>(new IOException("Missing video"))
        });
        await controller.StartAsync(Request("current"), Left);
        await controller.StartAsync(Request("other"), Right);
        controller.Pause();
        await Assert.ThrowsAsync<IOException>(() => controller.StartAsync(Request("failed"), Left));
        Assert.Equal("current", controller.ActiveRequests[Left.DeviceId].Id);
        Assert.Equal("current", controller.DesiredRequests[Left.DeviceId].Id);
        Assert.False(current.Disposed);
        Assert.False(other.Disposed);
        Assert.True(controller.IsPaused);
        if (failDuringShow) Assert.True(failed.Disposed);
    }

    [Fact]
    public async Task DifferentDisplaysPrepareConcurrentlyWithoutCancellingEachOther()
    {
        var leftReady = Pending();
        var rightReady = Pending();
        using var controller = new DisplayWallpaperController((request, _) => request.Target!.Value.DeviceId == Left.DeviceId ? leftReady.Task : rightReady.Task);
        var startLeft = controller.StartAsync(Request("fire"), Left);
        var startRight = controller.StartAsync(Request("lotus"), Right);
        rightReady.SetResult(new FakeSession());
        await startRight;
        Assert.Single(controller.ActiveRequests);
        Assert.False(startLeft.IsCompleted);
        leftReady.SetResult(new FakeSession());
        await startLeft;
        Assert.Equal(2, controller.ActiveRequests.Count);
    }

    [Fact]
    public async Task NewSelectionCancelsOnlyOlderStartOnSameDisplay()
    {
        var ready = Pending();
        var stale = new FakeSession();
        var other = new FakeSession();
        var latest = new FakeSession();
        using var controller = new DisplayWallpaperController((request, _) => request.Id == "slow" ? ready.Task :
            Task.FromResult<IWallpaperSession>(request.Id == "other" ? other : latest));
        var start = controller.StartAsync(Request("slow"), Left);
        await controller.StartAsync(Request("other"), Right);
        await controller.StartAsync(Request("latest"), Left);
        ready.SetResult(stale);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.True(stale.Disposed);
        Assert.False(stale.Shown);
        Assert.False(other.Disposed);
        Assert.Equal("latest", controller.DesiredRequests[Left.DeviceId].Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopClearsSavedAndPendingWorkBeforeLateCompletion(bool stopAll)
    {
        var ready = Pending();
        var stale = new FakeSession();
        var made = new List<FakeSession>();
        using var controller = new DisplayWallpaperController((request, _) => request.Id == "slow" ? ready.Task : Create(made));
        await controller.StartAsync(Request("left"), Left);
        await controller.StartAsync(Request("right"), Right);
        var start = controller.StartAsync(Request("slow"), Left);
        if (stopAll) controller.Stop(); else controller.Stop(Left.DeviceId);
        Assert.False(controller.DesiredRequests.ContainsKey(Left.DeviceId));
        if (stopAll) Assert.Empty(controller.DesiredRequests);
        ready.SetResult(stale);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await controller.ReconcileAsync([Left, Right]);
        Assert.False(controller.ActiveRequests.ContainsKey(Left.DeviceId));
        Assert.True(stale.Disposed);
        Assert.False(stale.Shown);
        Assert.Equal(!stopAll, controller.IsRunning);
        Assert.Equal(stopAll, made[1].Disposed);
    }

    [Fact]
    public async Task DisconnectCancelsFirstPendingApplyWithoutRememberingOrResurrectingIt()
    {
        var ready = Pending();
        var stale = new FakeSession();
        using var controller = new DisplayWallpaperController((_, _) => ready.Task);
        var start = controller.StartAsync(Request("slow"), Left);
        await controller.ReconcileAsync([Right]);
        ready.SetResult(stale);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await controller.ReconcileAsync([Left, Right]);
        Assert.Empty(controller.ActiveRequests);
        Assert.Empty(controller.DesiredRequests);
        Assert.True(stale.Disposed);
        Assert.False(stale.Shown);
    }

    [Fact]
    public async Task DisconnectDuringReplacementRestoresLastSuccessfulAssignmentOnReconnect()
    {
        var ready = Pending();
        var made = new List<FakeSession>();
        using var controller = new DisplayWallpaperController((request, _) => request.Id == "pending" ? ready.Task : Create(made));
        await controller.StartAsync(Request("saved"), Left);
        var start = controller.StartAsync(Request("pending"), Left);
        await controller.ReconcileAsync([Right]);
        Assert.False(controller.IsRunning);
        Assert.Equal("saved", controller.DesiredRequests[Left.DeviceId].Id);
        ready.SetResult(new FakeSession());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await controller.ReconcileAsync([Right, Left]);
        Assert.Equal("saved", controller.ActiveRequests[Left.DeviceId].Id);
        Assert.Equal(2, made.Count);
    }

    [Fact]
    public async Task PauseTargetsWholeSessionAndCombinesWithGlobalPauseWithoutEvents()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        var events = 0;
        controller.StateChanged += () => events++;
        controller.SetPausedDisplays([Right.DeviceId.ToUpperInvariant()]);
        Assert.False(made[0].Paused);
        Assert.True(made[1].Paused);
        Assert.False(controller.IsPaused);
        Assert.True(controller.IsDisplayPaused(Right.DeviceId));
        controller.Pause();
        Assert.True(controller.IsPaused);
        controller.Resume();
        Assert.False(made[0].Paused);
        Assert.True(made[1].Paused);
        controller.SetPausedDisplays([]);
        Assert.False(made[1].Paused);
        Assert.Equal(0, events);
        Assert.All(made, session => Assert.Equal(0, session.IndexPauseCalls));
    }

    [Fact]
    public async Task PauseAudioAndFrameCapChangesApplyToPendingAndFutureSessions()
    {
        var ready = Pending();
        var first = new FakeSession();
        var next = new FakeSession();
        using var controller = new DisplayWallpaperController((request, _) => request.Id == "pending" ? ready.Task : Task.FromResult<IWallpaperSession>(next));
        var start = controller.StartAsync(Request("pending"), Left);
        controller.SetFrameCap(45);
        controller.SetAudioEnabled(false);
        controller.SetPausedDisplays([Left.DeviceId]);
        ready.SetResult(first);
        await start;
        await controller.StartAsync(Request("next"), Right);
        Assert.Equal(45, first.FrameCap);
        Assert.False(first.AudioEnabled);
        Assert.True(first.PausedAtShow);
        Assert.Equal(45, next.FrameCap);
        Assert.False(next.AudioEnabled);
        Assert.False(next.Paused);
        Assert.Equal(45, controller.ActiveRequests[Left.DeviceId].FramesPerSecond);
        Assert.Equal(45, controller.DesiredRequests[Right.DeviceId].FramesPerSecond);
    }

    [Fact]
    public async Task SuccessfulReplacementRevealsBeforeOldSessionDisposalAndRetainsGlobalPause()
    {
        var old = new FakeSession();
        var next = new FakeSession { OnShow = () => Assert.False(old.Disposed) };
        using var controller = new DisplayWallpaperController((request, _) => Task.FromResult<IWallpaperSession>(request.Id == "old" ? old : next));
        await controller.StartAsync(Request("old"), Left);
        controller.Pause();
        await controller.StartAsync(Request("new"), Left);
        Assert.True(old.Disposed);
        Assert.True(next.PausedAtShow);
        Assert.True(controller.IsPaused);
    }

    [Fact]
    public async Task EditingSettingsAffectsOnlyAssignedDisplayAndIsPreservedWhenDisconnected()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        var original = VisualizerSettings.Default;
        var changed = original with { Scale = 0.7f, OffsetX = -0.2f };
        await controller.StartAsync(Request("same-wallpaper", original), Left);
        await controller.StartAsync(Request("same-wallpaper", original), Right);
        controller.UpdateVisualizerSettings(Left.DeviceId, changed);
        Assert.Equal(changed, made[0].Settings);
        Assert.Equal(original, made[1].Settings);
        await controller.ReconcileAsync([Right]);
        var offlineEdit = changed with { OffsetY = 0.3f };
        controller.UpdateVisualizerSettings(Left.DeviceId, offlineEdit);
        controller.SetFrameCap(24);
        await controller.ReconcileAsync([Right, Left]);
        Assert.Equal(offlineEdit, made[2].Settings);
        Assert.Equal(24, made[2].FrameCap);
        Assert.Equal(offlineEdit, controller.ActiveRequests[Left.DeviceId].Settings);
        Assert.Equal(original, controller.ActiveRequests[Right.DeviceId].Settings);
    }

    [Fact]
    public async Task PendingSessionUsesLatestSettingsForItsDisplay()
    {
        var ready = Pending();
        var session = new FakeSession();
        using var controller = new DisplayWallpaperController((_, _) => ready.Task);
        var start = controller.StartAsync(Request("slow", VisualizerSettings.Default), Left);
        var latest = VisualizerSettings.Default with { Intensity = 1.7f };
        controller.UpdateVisualizerSettings(Left.DeviceId, latest);
        ready.SetResult(session);
        await start;
        Assert.Equal(latest, session.Settings);
        Assert.Equal(latest, controller.DesiredRequests[Left.DeviceId].Settings);
    }

    [Fact]
    public async Task ReorderingOrRenamingDisplayNumbersDoesNotRestartStableAssignments()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        await controller.ReconcileAsync([Right with { DeviceName = "DISPLAY1" }, Left with { DeviceName = "DISPLAY2" }]);
        Assert.Equal(2, made.Count);
        Assert.All(made, session => Assert.False(session.Disposed));
        Assert.Equal("a", controller.ActiveRequests[Left.DeviceId].Id);
        Assert.Equal("b", controller.ActiveRequests[Right.DeviceId].Id);
    }

    [Fact]
    public async Task ChangedBoundsOrFailedSessionRestartsOnlyAffectedDisplay()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        var resized = Left with { Width = 1600, Height = 900, X = 20 };
        await controller.ReconcileAsync([resized, Right]);
        Assert.True(made[0].Disposed);
        Assert.False(made[1].Disposed);
        Assert.Equal(resized, controller.ActiveRequests[Left.DeviceId].Target);
        made[1].Healthy = false;
        Assert.False(controller.IsHealthy);
        await controller.ReconcileAsync([resized, Right]);
        Assert.True(made[1].Disposed);
        Assert.False(made[2].Disposed);
        Assert.True(controller.IsHealthy);
        Assert.Equal(4, made.Count);
    }

    [Fact]
    public async Task RecoveryFailureIsIsolatedAndOtherDisplaysStillRecover()
    {
        var made = new List<FakeSession>();
        var failLeft = false;
        using var controller = new DisplayWallpaperController((request, _) => failLeft && request.Target!.Value.DeviceId == Left.DeviceId
            ? Task.FromException<IWallpaperSession>(new IOException("Bad decoder")) : Create(made));
        await controller.StartAsync(Request("left"), Left);
        await controller.StartAsync(Request("right"), Right);
        made[0].Healthy = made[1].Healthy = false;
        failLeft = true;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => controller.ReconcileAsync([Left, Right]));
        Assert.Single(failure.InnerExceptions);
        Assert.Contains(Left.DeviceId, failure.InnerExceptions[0].Message);
        Assert.False(made[0].Disposed);
        Assert.True(made[1].Disposed);
        Assert.Equal(3, made.Count);
        Assert.Equal(2, controller.DesiredRequests.Count);
        failLeft = false;
        await controller.ReconcileAsync([Left, Right]);
        Assert.True(controller.IsHealthy);
        Assert.Equal(4, made.Count);
    }

    [Fact]
    public async Task ForcedExplorerRecoveryRebuildsAllConnectedButNotDisconnectedDisplays()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        await controller.ReconcileAsync([Left], force: true);
        Assert.Equal(3, made.Count);
        Assert.True(made[0].Disposed);
        Assert.True(made[1].Disposed);
        Assert.Single(controller.ActiveRequests);
        Assert.Equal(2, controller.DesiredRequests.Count);
    }

    [Fact]
    public async Task PolicyUpdatesFromLifecycleEventsDoNotSkipRemainingRecoveryAssignments()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        var changedSettings = VisualizerSettings.Default with { OffsetY = 0.4f };
        controller.StateChanged += () =>
        {
            controller.SetFrameCap(24);
            controller.SetAudioEnabled(false);
            controller.UpdateVisualizerSettings(Right.DeviceId, changedSettings);
        };
        await controller.ReconcileAsync([Left, Right], force: true);
        Assert.Equal(4, made.Count);
        Assert.True(made[0].Disposed);
        Assert.True(made[1].Disposed);
        Assert.Equal(24, made[3].FrameCap);
        Assert.Equal(changedSettings, made[3].Settings);
        Assert.False(made[3].AudioEnabled);
    }

    [Fact]
    public async Task DisplayStoppedWhileAnotherDisplayRecoversIsNotRestoredByOldPlan()
    {
        var ready = Pending();
        var recovering = false;
        var made = new List<FakeSession>();
        using var controller = new DisplayWallpaperController((request, _) => recovering && request.Target!.Value.DeviceId == Left.DeviceId
            ? ready.Task : Create(made));
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        recovering = true;
        var recovery = controller.ReconcileAsync([Left, Right], force: true);
        controller.Stop(Right.DeviceId);
        ready.SetResult(new FakeSession());
        await recovery;
        Assert.Single(controller.DesiredRequests);
        Assert.Single(controller.ActiveRequests);
        Assert.False(controller.DesiredRequests.ContainsKey(Right.DeviceId));
        Assert.Equal(2, made.Count);
    }

    [Fact]
    public async Task RecoveryCannotOverrideAUserSelectionAlreadyPreparing()
    {
        var ready = Pending();
        var made = new List<FakeSession>();
        using var controller = new DisplayWallpaperController((request, _) => request.Id == "new" ? ready.Task : Create(made));
        await controller.StartAsync(Request("old"), Left);
        var start = controller.StartAsync(Request("new"), Left);
        await controller.ReconcileAsync([Left], force: true);
        Assert.Single(made);
        ready.SetResult(new FakeSession());
        await start;
        Assert.Equal("new", controller.ActiveRequests[Left.DeviceId].Id);
    }

    [Fact]
    public async Task StopDuringRecoveryCancelsTheWholeRecoveryPlan()
    {
        var ready = Pending();
        var recovering = false;
        var made = new List<FakeSession>();
        using var controller = new DisplayWallpaperController((_, _) => recovering ? ready.Task : Create(made));
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        recovering = true;
        var recovery = controller.ReconcileAsync([Left, Right], force: true);
        controller.Stop();
        var late = new FakeSession();
        ready.SetResult(late);
        await recovery;
        Assert.Empty(controller.DesiredRequests);
        Assert.False(controller.IsRunning);
        Assert.False(late.Shown);
        Assert.True(late.Disposed);
    }

    [Fact]
    public async Task ReentrantStopDuringShowCannotRestoreDesiredAssignment()
    {
        DisplayWallpaperController? controller = null;
        var session = new FakeSession { OnShow = () => controller!.Stop() };
        using (controller = new DisplayWallpaperController((_, _) => Task.FromResult<IWallpaperSession>(session)))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.StartAsync(Request("a"), Left));
            Assert.Empty(controller.DesiredRequests);
            Assert.False(controller.IsRunning);
            Assert.True(session.Disposed);
        }
    }

    [Fact]
    public async Task DisposeCleansAllSessionsAndPendingWorkAndRejectsFutureStarts()
    {
        var ready = Pending();
        var made = new List<FakeSession>();
        var controller = new DisplayWallpaperController((request, _) => request.Id == "slow" ? ready.Task : Create(made));
        await controller.StartAsync(Request("a"), Left);
        await controller.StartAsync(Request("b"), Right);
        var start = controller.StartAsync(Request("slow"), Left);
        controller.Dispose();
        controller.Dispose();
        var late = new FakeSession();
        ready.SetResult(late);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.All(made, session => Assert.Equal(1, session.DisposeCount));
        Assert.True(late.Disposed);
        Assert.Empty(controller.DesiredRequests);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.StartAsync(Request("new"), Left));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => controller.ReconcileAsync([Left]));
    }

    [Fact]
    public async Task MissingIdentityOrInvalidBoundsCannotStartOrRememberAssignment()
    {
        var made = new List<FakeSession>();
        using var controller = Controller(made);
        await Assert.ThrowsAsync<ArgumentException>(() => controller.StartAsync(Request("a"), Left with { DeviceId = "" }));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.StartAsync(Request("a"), Left with { Width = 0 }));
        Assert.Empty(made);
        Assert.Empty(controller.DesiredRequests);
    }

    [Fact]
    public async Task ProcessIdsAndHealthReflectOnlyActiveSessions()
    {
        var session = new FakeSession { ProcessId = 8123 };
        using var controller = new DisplayWallpaperController((_, _) => Task.FromResult<IWallpaperSession>(session));
        Assert.False(controller.IsRunning);
        Assert.False(controller.IsHealthy);
        Assert.False(controller.IsPaused);
        await controller.StartAsync(Request("video"), Left);
        Assert.Equal([8123], controller.ActiveProcessIds);
        await controller.ReconcileAsync([]);
        Assert.Empty(controller.ActiveProcessIds);
        Assert.False(controller.IsRunning);
    }

    private static DisplayWallpaperController Controller(List<FakeSession> made)
        => new((_, _) => Create(made));

    private static Task<IWallpaperSession> Create(List<FakeSession> made)
    {
        var session = new FakeSession();
        made.Add(session);
        return Task.FromResult<IWallpaperSession>(session);
    }

    private sealed class FakeSession : IWallpaperSession
    {
        public bool Disposed => DisposeCount > 0;
        public int DisposeCount { get; private set; }
        public bool Shown { get; private set; }
        public bool Paused { get; private set; }
        public bool PausedAtShow { get; private set; }
        public bool AudioEnabled { get; private set; }
        public int FrameCap { get; private set; }
        public int IndexPauseCalls { get; private set; }
        public bool Healthy { get; set; } = true;
        public Action? OnShow { get; init; }
        public VisualizerSettings? Settings { get; private set; }
        public bool IsHealthy => !Disposed && Healthy;
        public int? ProcessId { get; init; }
        public void Show() { OnShow?.Invoke(); PausedAtShow = Paused; Shown = true; }
        public void Dispose() => DisposeCount++;
        public void Pause() => Paused = true;
        public void Resume() => Paused = false;
        public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) => IndexPauseCalls++;
        public void SetFrameCap(int framesPerSecond) => FrameCap = framesPerSecond;
        public void UpdateVisualizerSettings(VisualizerSettings settings) => Settings = settings;
        public void SetAudioEnabled(bool enabled) => AudioEnabled = enabled;
    }
}
