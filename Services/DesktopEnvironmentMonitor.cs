using Microsoft.Win32;
using System.Windows.Threading;

namespace AnimatedWallPaper.Services;

internal sealed class DesktopEnvironmentMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _disposed;
    private bool _sessionLocked;
    private bool _suspended;
    public bool SessionLocked => _sessionLocked || _suspended;
    public event Action? LayoutChanged;
    public event Action? PolicyChanged;
    public event Action? HealthCheck;

    public DesktopEnvironmentMonitor()
    {
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        _timer.Tick += (_, _) => HealthCheck?.Invoke();
        _timer.Start();
    }

    private void Dispatch(Action action) => _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatch(() => LayoutChanged?.Invoke());
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatch(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect)
            _sessionLocked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect)
            _sessionLocked = false;
        PolicyChanged?.Invoke();
        if (!SessionLocked) LayoutChanged?.Invoke();
    });
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) => Dispatch(() =>
    {
        if (e.Mode == PowerModes.Suspend) _suspended = true;
        if (e.Mode == PowerModes.Resume) { _suspended = false; LayoutChanged?.Invoke(); }
        PolicyChanged?.Invoke();
    });

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
    }
}
