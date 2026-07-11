using System.Diagnostics;
using System.IO;

namespace AnimatedWallPaper.Services;

internal sealed class VideoWallpaperSession : IWallpaperSession
{
    private const int DecodeWidth = 1280;
    private const int DecodeHeight = 720;
    private readonly Process _decoder;
    private readonly NativeWallpaperHost _host;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _readerTask;

    public int? ProcessId => null;

    public VideoWallpaperSession(string videoPath)
    {
        if (!File.Exists(videoPath))
        {
            throw new FileNotFoundException("Example video was not found.", videoPath);
        }

        _host = new NativeWallpaperHost(NativeRenderMode.Video, DesktopWorker.GetMonitorTargets());
        DesktopWorker.AttachWallpaperWindow(_host.Handle);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-stream_loop", "-1", "-re", "-i", videoPath,
                     "-an", "-vf", $"scale={DecodeWidth}:{DecodeHeight}:force_original_aspect_ratio=increase,crop={DecodeWidth}:{DecodeHeight}",
                     "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        _decoder = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _decoder.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) AppLog.Write($"ffmpeg: {args.Data}");
        };
        if (!_decoder.Start()) throw new InvalidOperationException("Unable to start ffmpeg.");
        _decoder.BeginErrorReadLine();

        _readerTask = Task.Run(() => ReadFramesAsync(_cancellation.Token));
        _host.Start(30);
        AppLog.Write($"Native video wallpaper started. Path={videoPath}; DecoderPID={_decoder.Id}; Decode={DecodeWidth}x{DecodeHeight}");
    }

    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        var frame = new byte[DecodeWidth * DecodeHeight * 4];
        var stream = _decoder.StandardOutput.BaseStream;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var offset = 0;
                while (offset < frame.Length)
                {
                    var read = await stream.ReadAsync(frame.AsMemory(offset, frame.Length - offset), cancellationToken);
                    if (read == 0) return;
                    offset += read;
                }
                _host.SubmitVideoFrame(frame, DecodeWidth, DecodeHeight);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.WriteException("Video frame reader failed", exception); }
    }

    public void SetFrameCap(int framesPerSecond) => _host.SetFrameCap(framesPerSecond);
    public void Pause() => _host.Pause();
    public void Resume() => _host.Resume();
    public void SetPausedMonitor(int? monitorIndex) => _host.SetPausedMonitor(monitorIndex);
    public void UpdateVisualizerSettings(VisualizerSettings settings) { }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (!_decoder.HasExited) _decoder.Kill(true);
        try { _readerTask.Wait(2000); } catch { }
        _decoder.Dispose();
        _cancellation.Dispose();
        _host.Dispose();
        AppLog.Write("Native video wallpaper stopped");
    }
}
