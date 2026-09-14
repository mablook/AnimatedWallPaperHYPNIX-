using System.Threading.Tasks;

namespace AnimatedWallPaper;

public partial class App : System.Windows.Application
{
    public App()
    {
        Services.AppLog.Initialize();
        DispatcherUnhandledException += (_, args) =>
        {
            Services.AppLog.WriteException("Unhandled UI exception", args.Exception);
            // HYPNIX lives in the tray and keeps a wallpaper running; recover from non-fatal
            // UI faults instead of tearing down the process. Corrupting faults still terminate.
            args.Handled = !IsFatal(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Services.AppLog.WriteException(
                $"Unhandled domain exception (terminating={args.IsTerminating})",
                args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        // Observe faults from fire-and-forget tasks (audio/video workers) so a stray
        // continuation cannot escalate into a process-level failure; they are logged instead.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.AppLog.WriteException("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    // Exceptions that indicate process-level corruption or resource exhaustion; swallowing
    // these would leave the app in an undefined state, so they are allowed to terminate.
    private static bool IsFatal(Exception exception) => exception is OutOfMemoryException
        or StackOverflowException or AccessViolationException or System.Threading.ThreadAbortException;
}
