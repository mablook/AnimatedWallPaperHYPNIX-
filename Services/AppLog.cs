using System.IO;

namespace AnimatedWallPaper.Services;

internal static class AppLog
{
    private static readonly object Sync = new();
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "logs");

    public static string FilePath => Path.Combine(LogDirectory, "wallpaper.log");

    public static void Initialize()
    {
        Write($"Application started. OS={Environment.OSVersion}; PID={Environment.ProcessId}; BaseDirectory={AppContext.BaseDirectory}");
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= 2 * 1024 * 1024)
                {
                    for (var i = 3; i >= 1; i--)
                    {
                        var source = i == 1 ? FilePath : FilePath + "." + (i - 1);
                        if (File.Exists(source)) File.Move(source, FilePath + "." + i, true);
                    }
                }
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"HYPNIX log unavailable: {exception.Message}; {message}");
            }
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        Write($"{context}: {exception}");
    }
}
