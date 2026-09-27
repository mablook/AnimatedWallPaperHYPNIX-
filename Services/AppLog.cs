using System.IO;
using System.Reflection;

namespace AnimatedWallPaper.Services;

internal static class AppLog
{
    private static readonly object Sync = new();
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "logs");
    public static string FilePath => Path.Combine(LogDirectory, "wallpaper.log");
    public static string SessionId { get; } = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}";
    // Build-time opt-in: preserve each development session outside MSIX AppData redirection.
    public static string? DevelopmentLogDirectory { get; } = ResolveDevelopmentLogDirectory();
    public static string? DevelopmentFilePath => DevelopmentLogDirectory is { } directory
        ? Path.Combine(directory, $"wallpaper-{SessionId}.log") : null;
    public static string DiagnosticsDirectory => DevelopmentLogDirectory ?? LogDirectory;

    private static string? ResolveDevelopmentLogDirectory()
    {
        var configured = typeof(AppLog).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "HypnixDevelopmentLogDirectory")?.Value;
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured : null;
    }

    public static void Initialize()
    {
        Write($"Application started. Version={AppInstall.Version}; Build={AppInstall.BuildId}; " +
              $"Session={SessionId}; Packaged={AppInstall.IsPackaged}; OS={Environment.OSVersion}; " +
              $"PID={Environment.ProcessId}; BaseDirectory={AppContext.BaseDirectory}; " +
              $"Diagnostics={DiagnosticsDirectory}; DevelopmentLog={DevelopmentFilePath ?? "disabled"}");
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            var line = $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}";
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
                File.AppendAllText(FilePath, line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"HYPNIX log unavailable: {exception.Message}; {message}");
            }
            // Capture the test session even if the normal AppData destination failed.
            if (DevelopmentFilePath is { } developmentPath)
            {
                try
                {
                    Directory.CreateDirectory(DevelopmentLogDirectory!);
                    File.AppendAllText(developmentPath, line);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"HYPNIX development log unavailable: {exception.Message}");
                }
            }
        }
    }

    public static void WriteException(string context, Exception exception) => Write($"{context}: {exception}");
}