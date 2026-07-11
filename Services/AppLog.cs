using System.IO;
using System.Security.Principal;

namespace AnimatedWallPaper.Services;

internal static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    public static string FilePath => Path.Combine(LogDirectory, "wallpaper.log");

    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        File.WriteAllText(FilePath, string.Empty);

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        Write($"Application started. OS={Environment.OSVersion}; User={identity.Name}; " +
              $"Session={Environment.ProcessId}/{System.Diagnostics.Process.GetCurrentProcess().SessionId}; " +
              $"Administrator={principal.IsInRole(WindowsBuiltInRole.Administrator)}; BaseDirectory={AppContext.BaseDirectory}");
    }

    public static void Write(string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(LogDirectory);
            File.AppendAllText(FilePath, $"{DateTimeOffset.Now:O} | {message}{Environment.NewLine}");
        }
    }

    public static void WriteException(string context, Exception exception)
    {
        Write($"{context}: {exception}");
    }
}
