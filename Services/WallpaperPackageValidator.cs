using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnimatedWallPaper.Services;

internal static partial class WallpaperPackageValidator
{
    private const int MaximumFileCount = 64;
    private const long MaximumTotalBytes = 256L * 1024 * 1024;
    private const long MaximumManifestBytes = 64L * 1024;
    private static readonly HashSet<string> SupportedRenderers = new(StringComparer.OrdinalIgnoreCase)
    {
        "hypnix.visualizer.classic.v1"
    };
    private static readonly HashSet<string> KnownFutureRenderers = new(StringComparer.OrdinalIgnoreCase)
    {
        "hypnix.flame-fluid.v1"
    };
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".png", ".jpg", ".jpeg", ".webp"
    };

    public static WallpaperPackageRegistration Validate(string packageDirectory)
    {
        try
        {
            var packageRoot = Path.GetFullPath(packageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directory = new DirectoryInfo(packageRoot);
            if (!directory.Exists) return Invalid(packageRoot, "Package directory does not exist.");
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return Invalid(packageRoot, "Package directory cannot be a reparse point.");

            var pending = new Stack<DirectoryInfo>();
            pending.Push(directory);
            var entryCount = 0;
            long totalBytes = 0;
            while (pending.TryPop(out var current))
            {
                foreach (var entry in current.EnumerateFileSystemInfos())
                {
                    if (++entryCount > MaximumFileCount) return Invalid(packageRoot, "Package contains too many files or directories.");
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                        return Invalid(packageRoot, "Package entries cannot be reparse points.");
                    if (entry is DirectoryInfo child) { pending.Push(child); continue; }
                    var file = (FileInfo)entry;
                    totalBytes += file.Length;
                    if (totalBytes > MaximumTotalBytes) return Invalid(packageRoot, "Package exceeds the maximum installed size.");
                    if (!AllowedExtensions.Contains(file.Extension)) return Invalid(packageRoot, "Package contains a forbidden file type.");
                }
            }

            var manifestPath = Path.Combine(packageRoot, "wallpaper.json");
            var manifestFile = new FileInfo(manifestPath);
            if (!manifestFile.Exists || manifestFile.Length is <= 0 or > MaximumManifestBytes)
                return Invalid(packageRoot, "wallpaper.json is missing, empty, or too large.");

            var manifest = JsonSerializer.Deserialize<WallpaperPackageManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null) return Invalid(packageRoot, "Manifest is empty.");
            if (manifest.SchemaVersion != 1) return Invalid(packageRoot, "Unsupported schemaVersion.", manifest);
            if (string.IsNullOrWhiteSpace(manifest.Id) || !PackageIdPattern().IsMatch(manifest.Id))
                return Invalid(packageRoot, "Package id is invalid.", manifest);
            if (!string.Equals(directory.Name, manifest.Id, StringComparison.OrdinalIgnoreCase))
                return Invalid(packageRoot, "Package directory must match its manifest id.", manifest);
            if (string.IsNullOrWhiteSpace(manifest.Title) || manifest.Title.Length > 120)
                return Invalid(packageRoot, "Package title is missing or too long.", manifest);
            if (!string.Equals(manifest.Type, "native-preset", StringComparison.OrdinalIgnoreCase))
                return Invalid(packageRoot, "Only native-preset downloads are enabled in this phase.", manifest);
            if (string.IsNullOrWhiteSpace(manifest.RendererId))
                return Invalid(packageRoot, "rendererId is required.", manifest);

            if (!TryResolveAsset(packageRoot, manifest.Preview, out _, out var previewError))
                return Invalid(packageRoot, $"Invalid preview: {previewError}", manifest);
            if (!TryResolveAsset(packageRoot, manifest.Entrypoint, out _, out var entrypointError))
                return Invalid(packageRoot, $"Invalid entrypoint: {entrypointError}", manifest);
            if (!string.IsNullOrWhiteSpace(manifest.Background) &&
                !TryResolveAsset(packageRoot, manifest.Background, out _, out var backgroundError))
                return Invalid(packageRoot, $"Invalid background: {backgroundError}", manifest);

            if (SupportedRenderers.Contains(manifest.RendererId))
                return new WallpaperPackageRegistration(packageRoot, WallpaperPackageStatus.Ready, manifest, null);
            if (KnownFutureRenderers.Contains(manifest.RendererId))
                return new WallpaperPackageRegistration(packageRoot, WallpaperPackageStatus.UnsupportedRenderer,
                    manifest, "Renderer is recognized but is not installed in this HYPNIX version.");
            return Invalid(packageRoot, "rendererId is not recognized.", manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid(Path.GetFullPath(packageDirectory), exception.Message);
        }
    }

    private static bool TryResolveAsset(string packageRoot, string? relativePath, out string? fullPath, out string? error)
    {
        fullPath = null;
        error = null;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            error = "Path is missing or rooted.";
            return false;
        }
        if (relativePath.Contains(':') || relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "Path contains invalid characters or an alternate stream.";
            return false;
        }

        var rootWithSeparator = packageRoot + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            error = "Path escapes the package directory.";
            return false;
        }
        if (!File.Exists(fullPath))
        {
            error = "File does not exist.";
            return false;
        }
        return true;
    }

    private static WallpaperPackageRegistration Invalid(
        string directory, string error, WallpaperPackageManifest? manifest = null) =>
        new(directory, WallpaperPackageStatus.Invalid, manifest, error);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();
}
