using System.Reflection;
using ComboBox = System.Windows.Controls.ComboBox;
using System.Windows.Threading;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class ForegroundAppMonitorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BackgroundDetectionUsesOwnerDispatcherBeforeApplicationRun(bool genericContext)
        => RunOnStaAsync(() =>
        {
            var ownerThread = Environment.CurrentManagedThreadId;
            var selection = new ComboBox { Items = { "First monitor", "Second monitor" }, SelectedIndex = 1 };
            // MainWindow field initializers run before Application.Run installs WPF's context.
            SynchronizationContext.SetSynchronizationContext(genericContext ? new SynchronizationContext() : null);
            using var monitor = new ForegroundAppMonitor();
            ForceStateChange(monitor);
            var notification = new TaskCompletionSource<(int Thread, int Selection, Exception? Failure)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            monitor.StateChanged += (_, _) =>
            {
                try { notification.TrySetResult((Environment.CurrentManagedThreadId, selection.SelectedIndex, null)); }
                catch (Exception exception) { notification.TrySetResult((Environment.CurrentManagedThreadId, -1, exception)); }
            };

            RunBackgroundTick(monitor);
            PumpUntil(() => notification.Task.IsCompleted);
            var result = notification.Task.GetAwaiter().GetResult();
            Assert.Equal(ownerThread, result.Thread);
            Assert.Null(result.Failure);
            Assert.Equal(1, result.Selection);
            Assert.DoesNotContain(-1, monitor.CoveredMonitors);
        });

    [Fact]
    public Task DisposalBeforeQueuedDetectionIsAppliedKeepsStateUnchanged()
        => RunOnStaAsync(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var monitor = new ForegroundAppMonitor();
            ForceStateChange(monitor);
            var notifications = 0;
            monitor.StateChanged += (_, _) => notifications++;

            // Detection finishes while this thread has not yet pumped its dispatcher.
            RunBackgroundTick(monitor);
            monitor.Dispose();
            DrainDispatcher();

            Assert.Equal(0, notifications);
            Assert.Equal(new[] { -1 }, monitor.CoveredMonitors);
        });

    [Fact]
    public Task RefreshNowAppliesStateSynchronouslyOnOwnerThread()
        => RunOnStaAsync(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var monitor = new ForegroundAppMonitor();
            ForceStateChange(monitor);
            var ownerThread = Environment.CurrentManagedThreadId;
            var notificationThread = 0;
            monitor.StateChanged += (_, _) => notificationThread = Environment.CurrentManagedThreadId;

            monitor.RefreshNow();

            Assert.Equal(ownerThread, notificationThread);
            Assert.DoesNotContain(-1, monitor.CoveredMonitors);
        });

    private static void ForceStateChange(ForegroundAppMonitor monitor)
        => typeof(ForegroundAppMonitor).GetField("<CoveredMonitors>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(monitor, new[] { -1 });

    private static void RunBackgroundTick(ForegroundAppMonitor monitor)
        => Task.Run(() => typeof(ForegroundAppMonitor).GetMethod("BackgroundTick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(monitor, null)).GetAwaiter().GetResult();

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(completed(), "Foreground detection did not reach its owner dispatcher.");
    }

    private static Task RunOnStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
