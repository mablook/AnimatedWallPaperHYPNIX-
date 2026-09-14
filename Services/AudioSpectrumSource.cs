namespace AnimatedWallPaper.Services;

// Desktop and visible preview share one capture stream. All lease changes run on the UI thread.
internal static class AudioSpectrumSource
{
    private static AudioSpectrumService? _service;
    private static int _leases;

    public static IDisposable Subscribe(Action<float[]> listener)
    {
        _service ??= new AudioSpectrumService();
        _service.BandsAvailable += listener;
        _leases++;
        _service.Start();
        return new Subscription(listener);
    }

    private sealed class Subscription(Action<float[]> listener) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service!.BandsAvailable -= listener;
            if (--_leases == 0)
            {
                _service.Dispose();
                _service = null;
            }
        }
    }
}
