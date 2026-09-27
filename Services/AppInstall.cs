using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AnimatedWallPaper.Services;

// Small process-wide facts about how HYPNIX is running. These drive channel-specific behavior:
// - IsPackaged decides between the MSIX StartupTask and the per-user registry Run key for
//   "Start with Windows", and between Store-managed updates and the in-app Velopack updater.
// - Version is shown in the tray so users can see which build they are on.
internal static partial class AppInstall
{
    // True when the process runs from an MSIX package (Microsoft Store or sideload); false for the
    // Velopack website install, the portable ZIP, or a dev build.
    public static bool IsPackaged { get; } = HasPackageIdentity();

    // Friendly SemVer for display (e.g. "1.2.0"), taken from the informational version and, failing
    // that, the assembly version. Build metadata after '+' is trimmed for a clean tray label.
    public static string Version { get; } = ResolveVersion();
    public static string BuildId { get; } = Metadata("HypnixBuildId") ?? "local";

    internal static string? Metadata(string key) => typeof(AppInstall).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == key)?.Value is { Length: > 0 } value ? value : null;

    private static bool HasPackageIdentity()
    {
        try
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            if (result == 15700) return false; // APPMODEL_ERROR_NO_PACKAGE: not a packaged process.
            if (result is 0 or 122) return true; // success / ERROR_INSUFFICIENT_BUFFER: packaged.
            AppLog.WriteException("Package identity lookup", new Win32Exception(result));
        }
        catch (Exception exception) { AppLog.WriteException("Package identity lookup failed", exception); }
        return false;
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(AppInstall).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
