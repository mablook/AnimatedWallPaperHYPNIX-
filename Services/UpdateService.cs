using Velopack;
using Velopack.Sources;

namespace AnimatedWallPaper.Services;

// Wraps Velopack's UpdateManager. It is only active when HYPNIX was installed via Velopack
// (IsInstalled); running from a dev build, the portable ZIP, or as a Store/MSIX package is a safe
// no-op (the Store manages its own updates). The GitHub repository is private, so its Releases are
// not usable as an end-user feed; updates are served from a public release website instead. Set
// UpdateFeedUrl to that host (it must serve releases.win.json and the .nupkg files, e.g. a static
// site, CDN, S3 or Azure Blob). The HYPNIX_UPDATE_FEED environment variable overrides it with a
// local folder to test the full install -> update cycle without publishing anything.
internal sealed class UpdateService
{
    // TODO: point this at your public release host before shipping the website installer.
    private const string UpdateFeedUrl = "https://updates.example.com/hypnix/win";
    private readonly UpdateManager _manager;

    public UpdateService()
    {
        var feed = Environment.GetEnvironmentVariable("HYPNIX_UPDATE_FEED");
        _manager = string.IsNullOrWhiteSpace(feed)
            ? new UpdateManager(new SimpleWebSource(UpdateFeedUrl))
            : new UpdateManager(feed);
    }

    public bool IsInstalled => _manager.IsInstalled;

    // Returns a downloaded update ready to apply, or null when there is nothing new, when the app is
    // not a Velopack install, or when the feed is unreachable (logged and treated as "no update", so
    // a failed check never disrupts a running wallpaper).
    public async Task<UpdateInfo?> CheckAndDownloadAsync()
    {
        if (!_manager.IsInstalled) return null;
        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null) return null;
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            return update;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Update check failed; keeping the current version", exception);
            return null;
        }
    }

    public void ApplyAndRestart(UpdateInfo update) => _manager.ApplyUpdatesAndRestart(update);
}
