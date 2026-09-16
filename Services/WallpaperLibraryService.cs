using System.IO;

namespace AnimatedWallPaper.Services;

internal sealed class WallpaperLibraryService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounceTimer;
    private readonly object _sync = new();       // guards _packages/_disposed with short critical sections only
    private readonly object _scanGate = new();   // serializes rescans so directory I/O never overlaps
    private IReadOnlyList<WallpaperPackageRegistration> _packages = [];
    private volatile bool _disposed;

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
        _watcher.Error += (_, args) =>
        {
            AppLog.WriteException("Library watcher overflow; rescanning", args.GetException());
            OnLibraryChanged(this, new FileSystemEventArgs(WatcherChangeTypes.All, PackagesDirectory, ""));
        };
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
        // Serialize scans on _scanGate (not _sync) so the potentially slow directory
        // enumeration/validation never runs while holding the lock the UI thread takes
        // in the Packages getter. _sync is only entered for the brief snapshot swap.
        lock (_scanGate)
        {
            if (_disposed) return;
            WallpaperPackageRegistration[] registrations;
            try
            {
                registrations = new DirectoryInfo(PackagesDirectory)
                    .EnumerateDirectories("*", SearchOption.TopDirectoryOnly)
                    .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(directory => WallpaperPackageValidator.Validate(directory.FullName))
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLog.WriteException("Library rescan failed; retaining last valid snapshot", exception);
                return;
            }

            lock (_sync)
            {
                if (_disposed) return;
                _packages = registrations;
            }

            AppLog.Write($"Wallpaper library scanned. Root={LibraryRoot}; Packages={registrations.Length}; " +
                         $"Ready={registrations.Count(item => item.Status == WallpaperPackageStatus.Ready)}; " +
                         $"Invalid={registrations.Count(item => item.Status == WallpaperPackageStatus.Invalid)}");
            // Raised outside _sync so subscriber work never runs under the snapshot lock.
            PackagesChanged?.Invoke(registrations);
        }
    }

    private void OnLibraryChanged(object sender, FileSystemEventArgs args)
    {
        lock (_sync) if (!_disposed) _debounceTimer.Change(350, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounceTimer.Dispose();
        }
    }
}
