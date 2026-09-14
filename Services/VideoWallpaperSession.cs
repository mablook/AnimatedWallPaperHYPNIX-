using System.Diagnostics;
using System.IO;

namespace AnimatedWallPaper.Services;

internal sealed class VideoWallpaperSession : IWallpaperSession
{
    private Process? _decoder;
    private NativeWallpaperHost? _host;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _readerTask;
    private bool _disposed;
    private volatile bool _readerFailed;
    public int? ProcessId => _decoder?.Id;
    public bool IsHealthy => !_disposed && !_readerFailed && _host?.IsHealthy == true && _decoder?.HasExited == false;

    private VideoWallpaperSession() { }

    public static async Task<IWallpaperSession> CreateAsync(WallpaperRequest request, CancellationToken token)
    {
        var path = request.VideoPath ?? throw new InvalidOperationException("Choose a video first.");
        var (width, height) = await MediaTools.ProbeAsync(path, request.MediaToolsDirectory, token);
        var executable = MediaTools.Resolve("ffmpeg", request.MediaToolsDirectory);
        token.ThrowIfCancellationRequested();
        var session = new VideoWallpaperSession();
        try
        {
            session._host = new NativeWallpaperHost(NativeRenderMode.Video,
                request.Preview is null ? DesktopWorker.GetMonitorTargets() : null, preview: request.Preview);
            if (request.Preview is null) DesktopWorker.AttachWallpaperWindow(session._host.Handle);
            session._decoder = new Process
            {
                StartInfo = MediaTools.StartInfo(executable,
                    ["-hide_banner", "-loglevel", "error", "-nostdin", "-stream_loop", "-1", "-re", "-i", path,
                     "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", $"scale={width}:{height},setsar=1",
                     "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"])
            };
            session._decoder.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data)) AppLog.Write($"ffmpeg: {args.Data}");
            };
            if (!session._decoder.Start()) throw new InvalidOperationException("Unable to start ffmpeg.");
            session._decoder.BeginErrorReadLine();
            session._readerTask = Task.Run(() => session.ReadFramesAsync(width, height, session._cancellation.Token));
            await session._firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            token.ThrowIfCancellationRequested();
            session._host.Start(request.FramesPerSecond, reveal: false);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private async Task ReadFramesAsync(int width, int height, CancellationToken token)
    {
        var frame = new byte[checked(width * height * 4)];
        var stream = _decoder!.StandardOutput.BaseStream;
        try
        {
            while (!token.IsCancellationRequested)
            {
                // A paused reader fills only the bounded OS pipe, then the decoder blocks too.
                await _pauseGate.WaitAsync(token);
                var offset = 0;
                while (offset < frame.Length)
                {
                    var read = await stream.ReadAsync(frame.AsMemory(offset), token);
                    if (read == 0) throw new EndOfStreamException("The video decoder stopped before a complete frame was available.");
                    offset += read;
                }
                _host!.SubmitVideoFrame(frame, width, height);
                _firstFrame.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { _firstFrame.TrySetCanceled(token); }
        catch (Exception exception)
        {
            _readerFailed = true;
            _firstFrame.TrySetException(exception);
            AppLog.WriteException("Video decoder failed", exception);
        }
    }

    public void Show() => _host!.Show();
    public void SetFrameCap(int framesPerSecond) => _host!.SetFrameCap(framesPerSecond);
    public void Pause() { _pauseGate.Pause(); _host!.Pause(); }
    public void Resume() { _pauseGate.Resume(); _host!.Resume(); }
    public void SetPausedMonitors(IReadOnlyList<int> monitorIndices) => _host!.SetPausedMonitors(monitorIndices);
    public void UpdateVisualizerSettings(VisualizerSettings settings) { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        if (_decoder is not null) MediaTools.Terminate(_decoder);
        _host?.Dispose();
        if (_readerTask is null) ReleaseDecoder();
        else _ = _readerTask.ContinueWith(_ => ReleaseDecoder(), TaskScheduler.Default);
    }
    private void ReleaseDecoder() { _decoder?.Dispose(); _cancellation.Dispose(); }
}
