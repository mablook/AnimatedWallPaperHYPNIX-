namespace AnimatedWallPaper.Services;

// Desktop and visible previews share one stream per source.
internal static class AudioSpectrumSource
{
    private static readonly AudioSpectrumRouter Router = new(microphone => new AudioSpectrumService(microphone));

    internal static bool MicrophoneEnabled => Router.MicrophoneEnabled;
    public static IDisposable Subscribe(Action<float[]> listener) => Router.Subscribe(listener);
    public static void SetMicrophoneEnabled(bool enabled) => Router.SetMicrophoneEnabled(enabled);
}

// Source lifetimes are independent: an absent microphone never interrupts system audio.
// Only normalized frequency bands survive a callback; raw samples stay in each FFT stream.
internal sealed class AudioSpectrumRouter(Func<bool, IAudioSpectrumStream> createStream, Func<long>? clock = null)
{
    private const int BandCount = 64;
    private const long StaleAfterMilliseconds = 300;
    private readonly object _sync = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly List<Subscription> _subscriptions = [];
    private IAudioSpectrumStream? _system;
    private IAudioSpectrumStream? _microphone;
    private bool _microphoneEnabled;
    private readonly float[] _systemBands = new float[BandCount];
    private readonly float[] _microphoneBands = new float[BandCount];
    private long _systemUpdated = long.MinValue;
    private long _microphoneUpdated = long.MinValue;

    internal bool MicrophoneEnabled { get { lock (_sync) return _microphoneEnabled; } }

    public IDisposable Subscribe(Action<float[]> listener)
    {
        lock (_sync)
        {
            var subscription = new Subscription(this, listener);
            _subscriptions.Add(subscription);
            if (_system is null) StartStream(false);
            if (_microphoneEnabled && _microphone is null) StartStream(true);
            return subscription;
        }
    }

    public void SetMicrophoneEnabled(bool enabled)
    {
        lock (_sync)
        {
            _microphoneEnabled = enabled;
            if (enabled)
            {
                if (_subscriptions.Count > 0 && _microphone is null) StartStream(true);
            }
            else
            {
                StopStream(true);
                Publish(); // Clear the microphone contribution immediately, including during silence.
            }
        }
    }

    private void StartStream(bool microphone)
    {
        var stream = createStream(microphone);
        if (microphone) _microphone = stream;
        else _system = stream;
        stream.BandsAvailable += bands => OnBands(stream, microphone, bands);
        stream.Start();
    }

    private void OnBands(IAudioSpectrumStream stream, bool microphone, float[] bands)
    {
        lock (_sync)
        {
            // Reject queued callbacks from a disabled/disposed stream or a previous lease.
            if (!ReferenceEquals(stream, microphone ? _microphone : _system)) return;
            var destination = microphone ? _microphoneBands : _systemBands;
            for (var i = 0; i < BandCount; i++)
                destination[i] = i < bands.Length && float.IsFinite(bands[i]) ? Math.Clamp(bands[i], 0, 1) : 0;
            if (microphone) _microphoneUpdated = _clock();
            else _systemUpdated = _clock();
            Publish();
        }
    }

    private void Publish()
    {
        var now = _clock();
        var systemFresh = _systemUpdated != long.MinValue && now - _systemUpdated <= StaleAfterMilliseconds;
        var microphoneFresh = _microphone is not null && _microphoneUpdated != long.MinValue &&
            now - _microphoneUpdated <= StaleAfterMilliseconds;
        var mixed = new float[BandCount];
        for (var i = 0; i < BandCount; i++)
            // Taking the stronger band avoids doubling sound heard by both sources.
            mixed[i] = Math.Max(systemFresh ? _systemBands[i] : 0, microphoneFresh ? _microphoneBands[i] : 0);
        // Serializing these short renderer callbacks with disable/unsubscribe ensures a late
        // microphone callback cannot overwrite the cleared frame after the user turns it off.
        foreach (var subscription in _subscriptions.ToArray())
        {
            if (subscription.Disposed) continue;
            try { subscription.Listener((float[])mixed.Clone()); }
            catch (Exception exception) { AppLog.WriteException("Audio listener failed", exception); }
        }
    }

    private void StopStream(bool microphone)
    {
        var stream = microphone ? _microphone : _system;
        if (microphone)
        {
            _microphone = null;
            Array.Clear(_microphoneBands);
            _microphoneUpdated = long.MinValue;
        }
        else
        {
            _system = null;
            Array.Clear(_systemBands);
            _systemUpdated = long.MinValue;
        }
        // Dispose cancels the worker; endpoint teardown happens on that worker, avoiding
        // blocking the UI or waiting for a WASAPI callback while holding this lock.
        stream?.Dispose();
    }

    private sealed class Subscription(AudioSpectrumRouter owner, Action<float[]> listener) : IDisposable
    {
        public Action<float[]> Listener { get; } = listener;
        public bool Disposed { get; private set; }
        public void Dispose()
        {
            lock (owner._sync)
            {
                if (Disposed) return;
                Disposed = true;
                owner._subscriptions.Remove(this);
                if (owner._subscriptions.Count != 0) return;
                owner.StopStream(true);
                owner.StopStream(false);
            }
        }
    }
}
