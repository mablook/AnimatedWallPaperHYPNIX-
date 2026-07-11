using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace AnimatedWallPaper.Services;

internal sealed class AudioSpectrumService : IDisposable
{
    private const int FftSize = 2048;
    private const int BandCount = 64;
    private readonly object _sync = new();
    private readonly float[] _sampleWindow = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private WasapiLoopbackCapture? _capture;
    private int _sampleCount;
    private bool _disposed;
    private int _restartScheduled;

    public event Action<float[]>? BandsAvailable;

    public void Start() => StartCapture();

    private void StartCapture()
    {
        if (_disposed) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var capture = new WasapiLoopbackCapture(device);
            capture.DataAvailable += CaptureOnDataAvailable;
            capture.RecordingStopped += CaptureOnRecordingStopped;
            lock (_sync)
            {
                _capture?.Dispose();
                _capture = capture;
            }
            capture.StartRecording();
            Interlocked.Exchange(ref _restartScheduled, 0);
            AppLog.Write($"Audio loopback started. Device={device.FriendlyName}; Format={capture.WaveFormat}; " +
                         $"SampleRate={capture.WaveFormat.SampleRate}; Channels={capture.WaveFormat.Channels}; " +
                         $"Bits={capture.WaveFormat.BitsPerSample}");
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Audio loopback start failed", exception);
            ScheduleRestart();
        }
    }

    private void CaptureOnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (args.Exception is not null) AppLog.WriteException("Audio loopback stopped", args.Exception);
        else AppLog.Write("Audio loopback stopped; endpoint may have changed");
        ScheduleRestart();
    }

    private void ScheduleRestart()
    {
        if (_disposed || Interlocked.Exchange(ref _restartScheduled, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            if (!_disposed) StartCapture();
        });
    }

    private void CaptureOnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var capture = sender as WasapiLoopbackCapture;
        if (capture is null) return;
        var format = capture.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var frameSize = bytesPerSample * format.Channels;
        if (frameSize <= 0) return;

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

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if (format.BitsPerSample == 32 && format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible)
            return BitConverter.ToSingle(buffer, offset);
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
        var fft = new Complex[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            fft[i].X = _sampleWindow[i] * (float)FastFourierTransform.HammingWindow(i, FftSize);
        }
        FastFourierTransform.FFT(true, 11, fft);

        var next = new float[BandCount];
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
        _disposed = true;
        lock (_sync)
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= CaptureOnDataAvailable;
                _capture.RecordingStopped -= CaptureOnRecordingStopped;
                try { _capture.StopRecording(); } catch { }
                _capture.Dispose();
                _capture = null;
            }
        }
        AppLog.Write("Audio loopback disposed");
    }
}
