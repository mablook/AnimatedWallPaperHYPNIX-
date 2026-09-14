using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AnimatedWallPaper.Services;

internal static class MediaTools
{
    public static string Resolve(string name, string? configuredDirectory)
    {
        var directories = new[] { configuredDirectory, Path.Combine(AppContext.BaseDirectory, "MediaTools") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory.Trim('"'))) continue;
            var candidate = Path.Combine(directory.Trim('"'), name + ".exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException($"{name}.exe was not found. Use 'Media tools' to choose a folder containing ffmpeg.exe and ffprobe.exe.");
    }

    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task<(int Width, int Height)> ProbeAsync(string path, string? toolsDirectory, CancellationToken token)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The selected video is missing. Choose its new location or add it again.", path);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process
        {
            StartInfo = StartInfo(Resolve("ffprobe", toolsDirectory),
                ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,sample_aspect_ratio:stream_side_data=rotation", "-of", "json", path])
        };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Unable to start ffprobe.");
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new InvalidDataException("The video could not be opened: " + await error);
            using var document = JsonDocument.Parse(await output);
            var streams = document.RootElement.GetProperty("streams");
            if (streams.GetArrayLength() == 0) throw new InvalidDataException("The selected file has no video stream.");
            return ReadDisplayDimensions(streams[0]);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Reading the video information took too long."); }
        finally { Terminate(process); }
    }

    internal static (int Width, int Height) CalculateDecodeSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
            throw new InvalidDataException("Unsupported video dimensions.");
        var ratio = Math.Min(1d, Math.Min(1280d / width, 720d / height));
        return (Math.Max(2, (int)(width * ratio) / 2 * 2), Math.Max(2, (int)(height * ratio) / 2 * 2));
    }

    internal static (int Width, int Height) ReadDisplayDimensions(JsonElement stream)
    {
        var width = stream.GetProperty("width").GetInt32();
        var height = stream.GetProperty("height").GetInt32();
        if (stream.TryGetProperty("sample_aspect_ratio", out var sar))
        {
            var parts = sar.GetString()?.Split(':');
            if (parts is { Length: 2 } && int.TryParse(parts[0], out var numerator) &&
                int.TryParse(parts[1], out var denominator) && numerator > 0 && denominator > 0)
                width = checked((int)Math.Round(width * (double)numerator / denominator));
        }
        if (stream.TryGetProperty("side_data_list", out var sideData))
            foreach (var data in sideData.EnumerateArray())
                if (data.TryGetProperty("rotation", out var rotation) &&
                    Math.Abs(rotation.GetDouble()) % 180 is > 45 and < 135)
                { (width, height) = (height, width); break; }
        return CalculateDecodeSize(width, height);
    }

    public static void Terminate(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception exception) { AppLog.WriteException("Media process cleanup failed", exception); }
    }
}
