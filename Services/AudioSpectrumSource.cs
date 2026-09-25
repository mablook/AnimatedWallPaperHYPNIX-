namespace AnimatedWallPaper.Services;

// Desktop and visible previews share one stream per source.
internal static class AudioSpectrumSource
{
    private static readonly AudioSpectrumRouter Router =
        new((microphone, deviceId) => new AudioSpectrumService(microphone, deviceId));

    internal static bool MicrophoneEnabled => Router.MicrophoneEnabled;
    internal static bool SystemEnabled => Router.SystemEnabled;
    public static IDisposable Subscribe(Action<float[]> listener) => Router.Subscribe(listener);

    // Preferred entry point: choose which reactive inputs are analyzed. "Microphone" never opens
    // the system loopback, so microphone-only reaction cannot be nudged by system playback.
    public static void SetSource(AudioReactionSource source) => Router.SetSource(source);

    // Legacy helper retained for older call sites/tools: toggles only the microphone on top of the
    // system source. New code should prefer SetSource with an explicit AudioReactionSource.
    public static void SetMicrophoneEnabled(bool enabled) => Router.SetMicrophoneEnabled(enabled);

    // Pin a specific endpoint by its stable MMDevice id; null follows the current Windows default.
    public static void SetMicrophoneDevice(string? deviceId) => Router.SetMicrophoneDevice(deviceId);
    public static void SetSystemDevice(string? deviceId) => Router.SetSystemDevice(deviceId);
}

// Source lifetimes are independent: an absent microphone never interrupts system audio, and a
// microphone-only reaction never opens the system loopback. Only normalized frequency bands
// survive a callback; raw samples stay inside each FFT stream.
internal sealed class AudioSpectrumRouter(Func<bool, string?, IAudioSpectrumStream> createStream, Func<long>? clock = null)
{
    private const int BandCount = 64;
    private const long StaleAfterMilliseconds = 300;
    private readonly object _sync = new();
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly List<Subscription> _subscriptions = [];
    private IAudioSpectrumStream? _system;
    private IAudioSpectrumStream? _microphone;
    // System audio is the default reactive source; the microphone is opt-in.
    private bool _systemEnabled = true;
    private bool _microphoneEnabled;
    private string? _systemDeviceId;
    private string? _microphoneDeviceId;
    private readonly float[] _systemBands = new float[BandCount];
    private readonly float[] _microphoneBands = new float[BandCount];
    private long _systemUpdated = long.MinValue;
    private long _microphoneUpdated = long.MinValue;

    internal bool MicrophoneEnabled { get { lock (_sync) return _microphoneEnabled; } }
    internal bool SystemEnabled { get { lock (_sync) return _systemEnabled; } }

    public IDisposable Subscribe(Action<float[]> listener)
    {
        lock (_sync)
        {
            var subscription = new Subscription(this, listener);
            _subscriptions.Add(subscription);
            if (_systemEnabled && _system is null) StartStream(false);
            if (_microphoneEnabled && _microphone is null) StartStream(true);
            return subscription;
        }
    }

    // Apply an explicit source selection: each mode opens and analyzes only its selected inputs,
    // releasing the deselected source and clearing its residual contribution immediately.
    public void SetSource(AudioReactionSource source)
    {
        source = source.Normalize();
        lock (_sync)
        {
            SetEnabled(microphone: false, source.UsesSystem());
            SetEnabled(microphone: true, source.UsesMicrophone());
        }
    }

    // Legacy microphone-only toggle; the system source is left untouched.
    public void SetMicrophoneEnabled(bool enabled)
    {
        lock (_sync) SetEnabled(microphone: true, enabled);
    }

    public void SetMicrophoneDevice(string? deviceId) => SetDevice(microphone: true, deviceId);
    public void SetSystemDevice(string? deviceId) => SetDevice(microphone: false, deviceId);

    private void SetDevice(bool microphone, string? deviceId)
    {
        deviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        lock (_sync)
        {
            ref var current = ref (microphone ? ref _microphoneDeviceId : ref _systemDeviceId);
            if (current == deviceId) return;
            current = deviceId;
            var enabled = microphone ? _microphoneEnabled : _systemEnabled;
            var running = (microphone ? _microphone : _system) is not null;
            // Rebind an active source to the new endpoint; clear the old device's residual bands.
            if (!enabled || !running) return;
            StopStream(microphone);
            if (_subscriptions.Count > 0) StartStream(microphone);
            Publish();
        }
    }

    // Caller holds _sync. Starts or stops one source and, on disable, clears its residual bands so
    // a just-removed input cannot linger in the mix (including while the other source is silent).
    private void SetEnabled(bool microphone, bool enabled)
    {
        if (microphone)
        {
            if (_microphoneEnabled == enabled) return;
            _microphoneEnabled = enabled;
        }
        else
        {
            if (_systemEnabled == enabled) return;
            _systemEnabled = enabled;
        }

        if (enabled)
        {
            if (_subscriptions.Count > 0 && (microphone ? _microphone : _system) is null) StartStream(microphone);
        }
        else
        {
            StopStream(microphone);
            Publish();
        }
    }

    private void StartStream(bool microphone)
    {
        var stream = createStream(microphone, microphone ? _microphoneDeviceId : _systemDeviceId);
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
        var systemFresh = _system is not null && _systemUpdated != long.MinValue &&
            now - _systemUpdated <= StaleAfterMilliseconds;
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
