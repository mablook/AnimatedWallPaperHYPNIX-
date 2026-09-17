using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class WallpaperRenderWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedOutPreparationCanFinishLateWithoutRenderingOrReleasingResourcesEarly(bool failLate)
    {
        using var release = new ManualResetEventSlim();
        var rendered = 0;
        var cleaned = 0;
        var initializerThread = 0;
        var cleanupThread = 0;
        using var worker = new WallpaperRenderWorker(() =>
        {
            initializerThread = Environment.CurrentManagedThreadId;
            release.Wait();
            if (failLate) throw new IOException("Delayed device initialization failed.");
        }, () => Interlocked.Increment(ref rendered), () => 1, () =>
        {
            cleanupThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref cleaned);
        });
        try
        {
            Assert.Throws<TimeoutException>(() => worker.Start(TimeSpan.FromMilliseconds(50)));
            Assert.False(worker.Stop(TimeSpan.Zero));
            Assert.Equal(0, Volatile.Read(ref cleaned));
            worker.Signal(); // shutdown timed out, but callbacks must still be safe
        }
        finally { release.Set(); }
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(worker.Stop(TimeSpan.FromSeconds(5)));
        worker.Signal(); // callbacks after cleanup are harmless, too
        Assert.Equal(0, rendered);
        Assert.Equal(1, cleaned);
        Assert.Equal(initializerThread, cleanupThread);
    }

    [Fact]
    public async Task StopDuringAFrameDefersCleanupUntilThatFrameReturns()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = 0;
        using var worker = new WallpaperRenderWorker(() => { }, () =>
        {
            entered.TrySetResult();
            release.Wait();
        }, () => Timeout.Infinite, () => Interlocked.Increment(ref cleaned));
        try
        {
            worker.Start(TimeSpan.FromSeconds(5));
            worker.Signal();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(worker.Stop(TimeSpan.Zero));
            Assert.Equal(0, Volatile.Read(ref cleaned));
        }
        finally { release.Set(); }
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public async Task InitializationFailureIsReportedAndCleansUpOnce()
    {
        var cleaned = 0;
        var failure = new IOException("Device removed");
        using var worker = new WallpaperRenderWorker(() => throw failure,
            () => throw new InvalidOperationException("Must not render"), () => 1,
            () => Interlocked.Increment(ref cleaned));
        Assert.Same(failure, Assert.Throws<IOException>(() => worker.Start(TimeSpan.FromSeconds(5))));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(worker.Stop(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public void StoppingBeforeStartIsIdempotent()
    {
        var cleaned = 0;
        using var worker = new WallpaperRenderWorker(() => { }, () => { }, () => 1, () => cleaned++);
        Assert.True(worker.Stop(TimeSpan.Zero));
        Assert.True(worker.Stop(TimeSpan.Zero));
        worker.Signal();
        Assert.Throws<ObjectDisposedException>(() => worker.Start(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, cleaned);
    }
}
