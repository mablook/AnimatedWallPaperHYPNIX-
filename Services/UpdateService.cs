using Velopack;
using Velopack.Sources;

namespace AnimatedWallPaper.Services;

// Wraps Velopack's UpdateManager. It is only active when HYPNIX was installed via Velopack
// (IsInstalled); running from a dev build or the portable ZIP is a safe no-op. The feed is the
// GitHub Releases repository by default, but the HYPNIX_UPDATE_FEED environment variable can point
// at a local folder to test the full install -> update cycle without publishing anything.
internal sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/mablook/AnimatedWallPaperHYPNIX-";
    private readonly UpdateManager _manager;

    public UpdateService()
    {
        var feed = Environment.GetEnvironmentVariable("HYPNIX_UPDATE_FEED");
        _manager = string.IsNullOrWhiteSpace(feed)
            ? new UpdateManager(new GithubSource(RepositoryUrl, null, false))
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
