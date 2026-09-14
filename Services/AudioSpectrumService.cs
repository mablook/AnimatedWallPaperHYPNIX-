using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace AnimatedWallPaper.Services;

internal sealed class AudioSpectrumService : IDisposable
{
    private const int FftSize = 2048;
    // FFT length must be a power of two; the exponent is derived so FftSize can be retuned safely.
    private static readonly int FftExponent = System.Numerics.BitOperations.Log2(FftSize);
    private const int BandCount = 64;
    private readonly object _sync = new();
    private readonly float[] _sampleWindow = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _nextBands = new float[BandCount];
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private int _sampleCount;
    private volatile bool _disposed;
    private volatile bool _captureStopped;
    private volatile bool _captureFaultLogged;
    private volatile bool _startFaultLogged;

    public event Action<float[]>? BandsAvailable;

    public void Start()
    {
        if (!_disposed) _worker ??= Task.Run(() => RunAsync(_shutdown.Token));
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                // Retry forever so playback recovers if a device returns, but back off from
                // 1.5s toward 30s so a permanently absent endpoint is not polled/logged in a storm.
                await RetryWorker.RunAsync(StartCapture, TimeSpan.FromMilliseconds(1500), token,
                    maxInterval: TimeSpan.FromSeconds(30), backoffFactor: 2.0);
                while (!token.IsCancellationRequested && !_captureStopped)
                {
                    await Task.Delay(1500, token);
                    try
                    {
                        using var enumerator = new MMDeviceEnumerator();
                        using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (current.ID != _device?.ID) break;
                    }
                    catch (Exception exception)
                    {
                        AppLog.WriteException("Audio endpoint unavailable", exception);
                        break;
                    }
                }
                await Task.Delay(1500, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { ReleaseCapture(); }
    }

    private bool StartCapture()
    {
        if (_disposed) return true;
        ReleaseCapture();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var capture = new WasapiLoopbackCapture(device);
            _capture = capture;
            _sampleCount = 0;
            Array.Clear(_bands);
            _captureStopped = false;
            _captureFaultLogged = false;
            capture.DataAvailable += CaptureOnDataAvailable;
            capture.RecordingStopped += CaptureOnRecordingStopped;
            capture.StartRecording();
            AppLog.Write($"Audio loopback started. Device={device.FriendlyName}; Format={capture.WaveFormat}; " +
                         $"SampleRate={capture.WaveFormat.SampleRate}; Channels={capture.WaveFormat.Channels}; " +
                         $"Bits={capture.WaveFormat.BitsPerSample}");
            _startFaultLogged = false;
            return true;
        }
        catch (Exception exception)
        {
            // Log only the first failure of a streak; a permanently missing device must not
            // fill the log. The flag is cleared above once a start succeeds.
            if (!_startFaultLogged)
            {
                _startFaultLogged = true;
                AppLog.WriteException("Audio loopback start failed", exception);
            }
            ReleaseCapture();
            return false;
        }
    }

    private void CaptureOnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null) AppLog.WriteException("Audio loopback stopped", args.Exception);
        else AppLog.Write("Audio loopback stopped; endpoint may have changed");
        _captureStopped = true;
    }

    private void ReleaseCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is not null)
        {
            capture.DataAvailable -= CaptureOnDataAvailable;
            capture.RecordingStopped -= CaptureOnRecordingStopped;
            try { capture.StopRecording(); }
            catch (Exception exception) { AppLog.WriteException("Audio stop failed", exception); }
            try { capture.Dispose(); }
            catch (Exception exception) { AppLog.WriteException("Audio release failed", exception); }
        }
        _device?.Dispose();
        _device = null;
    }

    private void CaptureOnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var capture = sender as WasapiLoopbackCapture;
        if (_disposed || capture is null || capture != _capture) return;
        var format = capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var frameSize = bytesPerSample * format.Channels;
        if (frameSize <= 0) return;

        // NAudio raises this on a background capture thread; an escaping exception would
        // tear down the capture (RecordingStopped) and trigger a restart storm. Contain it.
        try
        {
            for (var offset = 0; offset + frameSize <= args.BytesRecorded; offset += frameSize)
            {
                float mono = 0;
                for (var channel = 0; channel < format.Channels; channel++)
                {
                    var sampleOffset = offset + channel * bytesPerSample;
                    mono += ReadSample(args.Buffer, sampleOffset, format);
                }
                mono /= Math.Max(1, format.Channels);
                _sampleWindow[_sampleCount++] = mono;
                if (_sampleCount == FftSize)
                {
                    Analyze(format.SampleRate);
                    Array.Copy(_sampleWindow, FftSize / 2, _sampleWindow, 0, FftSize / 2);
                    _sampleCount = FftSize / 2;
                }
            }
        }
        catch (Exception exception)
        {
            _sampleCount = 0;
            if (!_captureFaultLogged)
            {
                _captureFaultLogged = true;
                AppLog.WriteException("Audio frame processing failed; dropping buffer", exception);
            }
        }
    }

    internal static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible &&
            extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        if (format.BitsPerSample == 32 && isFloat)
        {
            var sample = BitConverter.ToSingle(buffer, offset);
            return float.IsFinite(sample) ? Math.Clamp(sample, -1, 1) : 0;
        }
        if (format.BitsPerSample == 16)
            return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (format.BitsPerSample == 24)
        {
            var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
            if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
            return value / 8388608f;
        }
        if (format.BitsPerSample == 32)
            return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        return 0;
    }

    private void Analyze(int sampleRate)
    {
        var fft = _fft;
        for (var i = 0; i < FftSize; i++)
        {
            fft[i].X = _sampleWindow[i] * (float)FastFourierTransform.HammingWindow(i, FftSize);
            fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftExponent, fft);

        var next = _nextBands;
        const double minFrequency = 35;
        var maxFrequency = Math.Min(18000, sampleRate / 2d);
        for (var band = 0; band < BandCount; band++)
        {
            var low = minFrequency * Math.Pow(maxFrequency / minFrequency, band / (double)BandCount);
            var high = minFrequency * Math.Pow(maxFrequency / minFrequency, (band + 1d) / BandCount);
            var firstBin = Math.Max(1, (int)(low * FftSize / sampleRate));
            var lastBin = Math.Min(FftSize / 2 - 1, Math.Max(firstBin, (int)(high * FftSize / sampleRate)));
            double peak = 0;
            for (var bin = firstBin; bin <= lastBin; bin++)
            {
                var magnitude = Math.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y);
                peak = Math.Max(peak, magnitude);
            }
            next[band] = Math.Clamp((float)(Math.Log10(1 + peak * 18) / 1.15), 0, 1);
        }

        lock (_sync)
        {
            for (var i = 0; i < BandCount; i++)
            {
                var smoothing = next[i] > _bands[i] ? 0.62f : 0.16f;
                _bands[i] += (next[i] - _bands[i]) * smoothing;
            }
            BandsAvailable?.Invoke((float[])_bands.Clone());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        // The worker owns WASAPI teardown; never wait for it while holding an FFT lock.
        if (_worker is null) _shutdown.Dispose();
        else _ = _worker.ContinueWith(_ => _shutdown.Dispose(), TaskScheduler.Default);
        AppLog.Write("Audio loopback disposed");
    }
}
