namespace AnimatedWallPaper.Services;

// Owns render-thread resources until the thread actually exits, including after a timed-out
// startup or shutdown. Readiness uses a Task so late completion never touches a disposed event.
internal sealed class WallpaperRenderWorker : IDisposable
{
    private readonly object _sync = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private readonly Action _cleanup;
    private bool _started;
    private bool _finished;
    private volatile bool _stopping;

    public WallpaperRenderWorker(Action initialize, Action render, Func<int> frameDelay, Action cleanup)
    {
        _cleanup = cleanup;
        _thread = new Thread(() => Run(initialize, render, frameDelay))
        {
            IsBackground = true,
            Name = "HypnixWallpaperRender"
        };
    }

    public Task Completion => _completion.Task;

    public void Start(TimeSpan timeout)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_finished, this);
            if (_started) throw new InvalidOperationException("Render worker already started.");
            _thread.Start();
            _started = true;
        }
        try { _ready.Task.WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch
        {
            RequestStop();
            throw;
        }
    }

    public void Signal()
    {
        lock (_sync)
        {
            if (!_finished) _signal.Set();
        }
    }

    private void RequestStop()
    {
        lock (_sync)
        {
            _stopping = true;
            if (!_finished) _signal.Set();
        }
    }

    public bool Stop(TimeSpan timeout)
    {
        RequestStop();
        lock (_sync)
        {
            if (!_started)
            {
                if (!_finished) Finish();
                return true;
            }
        }
        return _thread.Join(timeout);
    }

    private void Run(Action initialize, Action render, Func<int> frameDelay)
    {
        try
        {
            initialize();
            _ready.TrySetResult();
            while (!_stopping)
            {
                _signal.WaitOne(frameDelay());
                if (_stopping) break;
                render();
            }
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            // Observe a failure even when Start already timed out.
            _ = _ready.Task.Exception;
            AppLog.WriteException("Wallpaper render worker failed", exception);
        }
        finally { Finish(); }
    }

    private void Finish()
    {
        try { _cleanup(); }
        catch (Exception exception) { AppLog.WriteException("Render resource cleanup failed", exception); }
        finally
        {
            lock (_sync)
            {
                _finished = true;
                _signal.Dispose();
            }
            _completion.TrySetResult();
        }
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(5));
}
