namespace AnimatedWallPaper.Services;

// One owner performs retries until success or cancellation; failures cannot latch a flag.
internal static class RetryWorker
{
    public static async Task RunAsync(Func<bool> attempt, TimeSpan interval, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt()) return;
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }
}
