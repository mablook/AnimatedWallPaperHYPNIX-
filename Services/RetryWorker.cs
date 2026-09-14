namespace AnimatedWallPaper.Services;

// One owner performs retries until success or cancellation; failures cannot latch a flag.
internal static class RetryWorker
{
    public static async Task RunAsync(Func<bool> attempt, TimeSpan interval, CancellationToken cancellationToken,
        TimeSpan? maxInterval = null, double backoffFactor = 1.0)
    {
        var delay = interval;
        var ceiling = maxInterval ?? interval;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt()) return;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = NextDelay(delay, ceiling, backoffFactor);
        }
    }

    // Escalates the retry interval geometrically toward a ceiling so a permanently missing
    // resource (e.g. no audio endpoint) is polled sparsely instead of hammered at a fixed rate.
    // A factor of 1.0 preserves the original fixed-interval behavior.
    internal static TimeSpan NextDelay(TimeSpan current, TimeSpan max, double factor)
    {
        if (factor <= 1.0) return current;
        var scaled = TimeSpan.FromTicks((long)(current.Ticks * factor));
        return scaled > max ? max : scaled;
    }
}
