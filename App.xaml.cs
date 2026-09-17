using System.Threading;
using System.Threading.Tasks;

namespace AnimatedWallPaper;

// Owns process-wide single-instance handles; they are released in OnExit, the application
// teardown hook, so the type itself is not made IDisposable.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001",
    Justification = "Single-instance handles are released in OnExit, the application teardown path.")]
public partial class App : System.Windows.Application
{
    // One running instance per user session. A second launch signals the first to surface its
    // window (from the tray) and then exits, so copies never accumulate in the notification area.
    // The names are unqualified (per-logon-session), not "Global\", so each user gets one instance.
    private const string InstanceMutexName = "HYPNIX.SingleInstance.b3f1e2d4-8a76-4c95-9e12-7f0a6c5d4e3b";
    private const string ActivateEventName = "HYPNIX.Activate.b3f1e2d4-8a76-4c95-9e12-7f0a6c5d4e3b";
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateSignal;
    private readonly EventWaitHandle _stopListener = new(false, EventResetMode.ManualReset);

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
                args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject?.ToString()));
        // Observe faults from fire-and-forget tasks (audio/video workers) so a stray
        // continuation cannot escalate into a process-level failure; they are logged instead.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.AppLog.WriteException("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Another HYPNIX already runs in this session: ask it to surface its window, then exit
            // quietly so a duplicate window or a second tray icon never appears.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var running))
                {
                    running.Set();
                    running.Dispose();
                }
            }
            catch (Exception exception) { Services.AppLog.WriteException("Could not signal the running instance", exception); }
            Services.AppLog.Write("Second HYPNIX instance detected; surfaced the existing window and exited.");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        StartActivationListener();
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    // Background listener: when another launch signals the activate event, surface our window on
    // the UI thread. It stops cleanly when the app exits.
    private void StartActivationListener()
    {
        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _activateSignal!, _stopListener };
            while (true)
            {
                int index;
                try { index = WaitHandle.WaitAny(handles); }
                catch (ObjectDisposedException) { break; }
                if (index != 0) break; // stop signaled during shutdown
                Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.ShowMainWindow()));
            }
        })
        { IsBackground = true, Name = "HypnixActivationListener" };
        thread.Start();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _stopListener.Set();
        _activateSignal?.Dispose();
        _instanceMutex?.Dispose();
        _stopListener.Dispose();
        base.OnExit(e);
    }

    // Exceptions that indicate process-level corruption or resource exhaustion; swallowing
    // these would leave the app in an undefined state, so they are allowed to terminate.
    private static bool IsFatal(Exception exception) => exception is OutOfMemoryException
        or StackOverflowException or AccessViolationException or System.Threading.ThreadAbortException;
}
