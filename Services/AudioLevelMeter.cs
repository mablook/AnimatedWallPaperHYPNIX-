using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AnimatedWallPaper.Services;

// A transient microphone level meter for the Sound panel. It opens a short-lived shared-mode
// capture on one microphone purely to compute a live input level (peak), reporting a smoothed
// 0..1 value plus a status. It is microphone-only by design, so in combined mode a song playing
// on the computer cannot make the meter move. No audio is stored, replayed or transmitted, and
// the capture is released as soon as the meter is stopped or disposed.
internal sealed class AudioLevelMeter : IDisposable
{
    private readonly object _sync = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private CancellationTokenSource? _startCts;
    private bool _running;
    private bool _disposed;
    private float _smoothed;
    private MicrophoneStatus _status = MicrophoneStatus.Off;

    public event Action<float>? LevelChanged;
    public event Action<MicrophoneStatus>? StatusChanged;

    public MicrophoneStatus Status { get { lock (_sync) return _status; } }

    // Begin (or rebind) the meter on a microphone. deviceId null follows the Windows default input.
    public void Start(string? deviceId)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        CancellationToken token;
        lock (_sync)
        {
            if (_disposed) return;
            _running = true;
            _startCts?.Cancel();
            _startCts = new CancellationTokenSource();
            token = _startCts.Token;
        }
        // Open off the caller (UI) thread; device open + start can take tens of milliseconds.
        Task.Run(() => Open(normalized, token));
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_running && _capture is null) return;
            _running = false;
            _startCts?.Cancel();
        }
        ReleaseCapture();
        _smoothed = 0;
        SetLevel(0);
        SetStatus(MicrophoneStatus.Off);
    }

    private void Open(string? deviceId, CancellationToken token)
    {
        ReleaseCapture();
        if (token.IsCancellationRequested) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            if (enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Count == 0)
            {
                Settle(MicrophoneStatus.NoMicrophone);
                return;
            }

            MMDevice device;
            if (deviceId is null)
            {
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia))
                {
                    Settle(MicrophoneStatus.NoMicrophone);
                    return;
                }
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
            else
            {
                device = enumerator.GetDevice(deviceId);
                if (device is null || device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
                {
                    device?.Dispose();
                    Settle(MicrophoneStatus.Unavailable);
                    return;
                }
            }

            var capture = new WasapiCapture(device);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            lock (_sync)
            {
                if (token.IsCancellationRequested || !_running)
                {
                    capture.DataAvailable -= OnDataAvailable;
                    capture.RecordingStopped -= OnRecordingStopped;
                    capture.Dispose();
                    device.Dispose();
                    return;
                }
                _device = device;
                _capture = capture;
                _smoothed = 0;
            }
            capture.StartRecording();
            SetLevel(0);
            SetStatus(MicrophoneStatus.Listening);
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Microphone meter unavailable", exception);
            Settle(Classify(exception));
        }
    }

    private void Settle(MicrophoneStatus status)
    {
        _smoothed = 0;
        SetLevel(0);
        SetStatus(status);
    }

    // E_ACCESSDENIED (0x80070005) is Windows microphone privacy blocking the input; anything else
    // (including AUDCLNT_E_DEVICE_INVALIDATED) is reported as an unavailable device.
    private static MicrophoneStatus Classify(Exception exception)
        => exception is COMException com && com.HResult == unchecked((int)0x80070005)
            ? MicrophoneStatus.AccessBlocked
            : MicrophoneStatus.Unavailable;

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var capture = sender as WasapiCapture;
        if (_disposed || capture is null || capture != _capture) return;
        var format = capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var frameSize = bytesPerSample * format.Channels;
        if (frameSize <= 0) return;
        float peak = 0;
        try
        {
            for (var offset = 0; offset + frameSize <= args.BytesRecorded; offset += frameSize)
                for (var channel = 0; channel < format.Channels; channel++)
                {
                    var sample = Math.Abs(AudioSpectrumService.ReadSample(args.Buffer, offset + channel * bytesPerSample, format));
                    if (sample > peak) peak = sample;
                }
        }
        catch (Exception exception) { AppLog.WriteException("Microphone meter frame failed", exception); return; }

        // Perceptual curve so quiet speech is visible; fast attack, slower release like a VU meter.
        var target = Math.Clamp(MathF.Sqrt(peak), 0, 1);
        _smoothed += (target - _smoothed) * (target > _smoothed ? 0.5f : 0.15f);
        SetLevel(_smoothed);
        // Hysteresis avoids status flicker right at the noise floor.
        var speaking = _status == MicrophoneStatus.SoundDetected ? _smoothed > 0.03f : _smoothed > 0.06f;
        SetStatus(speaking ? MicrophoneStatus.SoundDetected : MicrophoneStatus.Listening);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (_disposed || sender != _capture) return;
        // An unexpected stop while running usually means the device was removed or invalidated.
        if (_running) Settle(args.Exception is null ? MicrophoneStatus.Unavailable : Classify(args.Exception));
    }

    private void ReleaseCapture()
    {
        WasapiCapture? capture;
        MMDevice? device;
        lock (_sync)
        {
            capture = _capture;
            device = _device;
            _capture = null;
            _device = null;
        }
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); }
            catch (Exception exception) { AppLog.WriteException("Microphone meter stop failed", exception); }
            try { capture.Dispose(); }
            catch (Exception exception) { AppLog.WriteException("Microphone meter release failed", exception); }
        }
        device?.Dispose();
    }

    private void SetLevel(float level)
    {
        if (!_disposed) LevelChanged?.Invoke(Math.Clamp(level, 0, 1));
    }

    private void SetStatus(MicrophoneStatus status)
    {
        lock (_sync)
        {
            if (_status == status) return;
            _status = status;
        }
        if (!_disposed) StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            _startCts?.Cancel();
        }
        ReleaseCapture();
    }
}
