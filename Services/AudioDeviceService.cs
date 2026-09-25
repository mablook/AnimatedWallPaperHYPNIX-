using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AnimatedWallPaper.Services;

// Lists capture (microphone) and render (system output) endpoints by their Windows friendly names,
// tracks the current default per flow, and raises DevicesChanged on connect/disconnect/default
// changes. Enumeration alone never opens or activates any endpoint: it only reads metadata.
internal sealed class AudioDeviceService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notifications;
    private readonly System.Threading.Timer _debounce;
    private volatile bool _disposed;

    // Coalesced notification: hotplug bursts (add + property + default) collapse into one refresh.
    public event Action? DevicesChanged;

    public AudioDeviceService()
    {
        _debounce = new System.Threading.Timer(_ => RaiseChanged(), null, Timeout.Infinite, Timeout.Infinite);
        _notifications = new NotificationClient(ScheduleChanged);
        try { _enumerator.RegisterEndpointNotificationCallback(_notifications); }
        catch (Exception exception) { AppLog.WriteException("Audio device notifications unavailable", exception); }
    }

    public IReadOnlyList<AudioDeviceInfo> GetMicrophones() => Enumerate(DataFlow.Capture);
    public IReadOnlyList<AudioDeviceInfo> GetOutputs() => Enumerate(DataFlow.Render);

    public string? DefaultMicrophoneId => TryGetDefaultId(DataFlow.Capture);
    public string? DefaultOutputId => TryGetDefaultId(DataFlow.Render);

    // Resolve a stored device id to its current friendly name, or null when it is absent/inactive,
    // so callers can surface "device unavailable" instead of silently switching selections.
    public string? TryGetMicrophoneName(string id) => TryGetName(DataFlow.Capture, id);
    public string? TryGetOutputName(string id) => TryGetName(DataFlow.Render, id);

    private AudioDeviceInfo[] Enumerate(DataFlow flow)
    {
        try
        {
            var defaultId = TryGetDefaultId(flow);
            var list = new List<AudioDeviceInfo>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try { list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId)); }
                catch (Exception exception) { AppLog.WriteException("Audio endpoint name unavailable", exception); }
                finally { device.Dispose(); }
            }
            return list.OrderByDescending(device => device.IsDefault)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Audio device enumeration failed", exception);
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    private string? TryGetDefaultId(DataFlow flow)
    {
        try
        {
            if (!_enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)) return null;
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (Exception exception)
        {
            AppLog.WriteException("Default audio endpoint unavailable", exception);
            return null;
        }
    }

    private string? TryGetName(DataFlow flow, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            using var device = _enumerator.GetDevice(id);
            return device is not null && device.DataFlow == flow && device.State == DeviceState.Active
                ? device.FriendlyName : null;
        }
        catch { return null; }
    }

    private void ScheduleChanged()
    {
        if (_disposed) return;
        try { _debounce.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private void RaiseChanged()
    {
        if (!_disposed) DevicesChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce.Dispose();
        try { _enumerator.UnregisterEndpointNotificationCallback(_notifications); }
        catch (Exception exception) { AppLog.WriteException("Audio device notification cleanup failed", exception); }
        _enumerator.Dispose();
    }

    // COM callbacks arrive on an MTA thread; each just pokes the debounce timer, and the coalesced
    // DevicesChanged is marshaled to the UI by the subscriber.
    private sealed class NotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
        public void OnDeviceAdded(string pwstrDeviceId) => changed();
        public void OnDeviceRemoved(string deviceId) => changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => changed();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
