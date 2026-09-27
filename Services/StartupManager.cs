using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace AnimatedWallPaper.Services;

internal enum StartupResult
{
    Enabled,
    Disabled,
    // Windows blocks the change (the user turned HYPNIX off under Task Manager ▸ Startup, or policy).
    BlockedByUser,
    Failed
}

// Registers "Start with Windows" through the mechanism that matches how HYPNIX is installed:
// - MSIX (Store/sideload): the packaged StartupTask declared in AppxManifest.xml. Windows owns the
//   final say, so the user can still disable it in Task Manager; we surface that as BlockedByUser.
// - Everything else (Velopack install, portable, dev): a per-user HKCU\...\Run value that relaunches
//   the executable with --startup so the app can come up minimized to the tray.
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "HYPNIX";
    // Must match the TaskId in packaging/msix/AppxManifest.xml.
    private const string StartupTaskId = "HypnixStartup";
    internal const string StartupArgument = "--startup";

    // True when this process was launched automatically at sign-in (used to come up in the tray
    // instead of popping the window). The registry Run path passes --startup; a packaged MSIX
    // StartupTask cannot, so we also inspect the WinRT activation kind when packaged (best effort).
    public static bool WasLaunchedAtStartup(IReadOnlyList<string> args)
    {
        if (args is not null && args.Any(argument => string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase)))
            return true;
        return AppInstall.IsPackaged && WasActivatedByStartupTask();
    }

    private static bool WasActivatedByStartupTask()
    {
        try
        {
            return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Startup activation lookup failed", exception);
            return false;
        }
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (AppInstall.IsPackaged)
        {
            try
            {
                var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
                return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch (Exception exception)
            {
                AppLog.WriteException("Startup task query failed", exception);
                return false;
            }
        }
        return IsRunKeyPresent();
    }

    public static async Task<StartupResult> SetEnabledAsync(bool enabled)
        => AppInstall.IsPackaged ? await SetPackagedAsync(enabled).ConfigureAwait(false) : SetRunKey(enabled);

    private static async Task<StartupResult> SetPackagedAsync(bool enabled)
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId).AsTask().ConfigureAwait(false);
            if (enabled)
            {
                var state = await task.RequestEnableAsync().AsTask().ConfigureAwait(false);
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy
                    ? StartupResult.Enabled
                    : StartupResult.BlockedByUser;
            }
            if (task.State is StartupTaskState.Enabled) task.Disable();
            return StartupResult.Disabled;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Startup task update failed", exception);
            return StartupResult.Failed;
        }
    }

    private static bool IsRunKeyPresent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is not null;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Startup registry query failed", exception);
            return false;
        }
    }

    private static StartupResult SetRunKey(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return StartupResult.Failed;
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable)) return StartupResult.Failed;
                key.SetValue(RunValueName, $"\"{executable}\" {StartupArgument}");
                return StartupResult.Enabled;
            }
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return StartupResult.Disabled;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Startup registry update failed", exception);
            return StartupResult.Failed;
        }
    }
}
