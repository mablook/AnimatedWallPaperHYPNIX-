namespace AnimatedWallPaper.Services;

internal sealed class AsyncPauseGate
{
    private readonly object _sync = new();
    private TaskCompletionSource? _resume;
    public void Pause() { lock (_sync) _resume ??= new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public void Resume()
    {
        lock (_sync) { _resume?.TrySetResult(); _resume = null; }
    }
    public Task WaitAsync(CancellationToken token)
    {
        lock (_sync) return _resume?.Task.WaitAsync(token) ?? Task.CompletedTask;
    }
}
