using System.IO;

namespace AnimatedWallPaper.Services;

internal sealed class WallpaperLibraryService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounceTimer;
    private readonly object _sync = new();
    private IReadOnlyList<WallpaperPackageRegistration> _packages = [];
    private bool _disposed;

    public WallpaperLibraryService(string? libraryRoot = null)
    {
        LibraryRoot = libraryRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HYPNIX", "Library");
        PackagesDirectory = Path.Combine(LibraryRoot, "packages");
        StagingDirectory = Path.Combine(LibraryRoot, ".staging");
        Directory.CreateDirectory(PackagesDirectory);
        Directory.CreateDirectory(StagingDirectory);

        _debounceTimer = new System.Threading.Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(PackagesDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName |
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnLibraryChanged;
        _watcher.Changed += OnLibraryChanged;
        _watcher.Deleted += OnLibraryChanged;
        _watcher.Renamed += OnLibraryChanged;
        Rescan();
    }

    public string LibraryRoot { get; }
    public string PackagesDirectory { get; }
    public string StagingDirectory { get; }
    public IReadOnlyList<WallpaperPackageRegistration> Packages
    {
        get { lock (_sync) return _packages.ToArray(); }
    }

    public event Action<IReadOnlyList<WallpaperPackageRegistration>>? PackagesChanged;

    public void Rescan()
    {
        if (_disposed) return;
        var registrations = new DirectoryInfo(PackagesDirectory)
            .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
            .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .Select(directory => WallpaperPackageValidator.Validate(directory.FullName))
            .ToArray();
        lock (_sync) _packages = registrations;
        AppLog.Write($"Wallpaper library scanned. Root={LibraryRoot}; Packages={registrations.Length}; " +
                     $"Ready={registrations.Count(item => item.Status == WallpaperPackageStatus.Ready)}; " +
                     $"Invalid={registrations.Count(item => item.Status == WallpaperPackageStatus.Invalid)}");
        PackagesChanged?.Invoke(registrations);
    }

    private void OnLibraryChanged(object sender, FileSystemEventArgs args)
    {
        if (!_disposed) _debounceTimer.Change(350, Timeout.Infinite);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
