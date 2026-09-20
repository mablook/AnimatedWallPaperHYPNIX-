namespace AnimatedWallPaper.Services;

/// <summary>Owns independent desktop sessions using stable display identities, never screen ordinals.</summary>
internal sealed class DisplayWallpaperController : IDisposable
{
    private sealed class DisplaySession
    {
        public IWallpaperSession? Session;
        public WallpaperRequest? Request;
        public CancellationTokenSource? Pending;
        public WallpaperRequest? PendingRequest;
        public bool Paused;
    }

    private readonly object _sync = new();
    private readonly Func<WallpaperRequest, CancellationToken, Task<IWallpaperSession>> _factory;
    private readonly Dictionary<string, DisplaySession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WallpaperRequest> _desired = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _assignmentVersions = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _pausedDisplays = new(StringComparer.OrdinalIgnoreCase);
    private bool _globallyPaused;
    private bool _audioEnabled = true;
    private int? _frameCap;
    private bool _disposed;
    private long _topologyVersion;
    private long _nextAssignmentVersion;

    public DisplayWallpaperController(Func<WallpaperRequest, CancellationToken, Task<IWallpaperSession>>? factory = null)
        => _factory = factory ?? WallpaperSessionFactory.CreateAsync;

    public event Action? StateChanged;

    public IReadOnlyDictionary<string, WallpaperRequest> ActiveRequests
    {
        get { lock (_sync) return _sessions.Where(pair => pair.Value.Request is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Request!, StringComparer.OrdinalIgnoreCase); }
    }

    // Disconnected displays remain here until explicitly stopped, and can regain their own wallpaper on return.
    public IReadOnlyDictionary<string, WallpaperRequest> DesiredRequests
    {
        get { lock (_sync) return new Dictionary<string, WallpaperRequest>(_desired, StringComparer.OrdinalIgnoreCase); }
    }

    public bool IsRunning { get { lock (_sync) return _sessions.Values.Any(value => value.Session is not null); } }
    public bool IsHealthy { get { lock (_sync) return IsRunning && _sessions.Values.All(value => value.Session?.IsHealthy != false); } }
    public bool IsPaused { get { lock (_sync) return IsRunning && _sessions.Values.Where(value => value.Session is not null).All(value => value.Paused); } }
    public bool AudioEnabled { get { lock (_sync) return _audioEnabled; } }
    public IReadOnlyList<int> ActiveProcessIds
    {
        get { lock (_sync) return _sessions.Values.Select(value => value.Session?.ProcessId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray(); }
    }

    public bool IsDisplayPaused(string deviceId)
    {
        lock (_sync) return _sessions.TryGetValue(deviceId, out var display) && display.Session is not null && display.Paused;
    }

    public Task StartAsync(WallpaperRequest request, DesktopWorker.WallpaperTarget target)
        => StartCoreAsync(request, target);

    private async Task StartCoreAsync(WallpaperRequest request, DesktopWorker.WallpaperTarget target,
        long? expectedAssignmentVersion = null, long? topologyVersion = null)
    {
        ValidateTarget(target);
        DisplaySession display;
        CancellationTokenSource? previousPending;
        using var pending = new CancellationTokenSource();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (topologyVersion.HasValue)
            {
                if (topologyVersion != _topologyVersion || !_desired.TryGetValue(target.DeviceId, out var desired) ||
                    !_assignmentVersions.TryGetValue(target.DeviceId, out var revision) || revision != expectedAssignmentVersion) return;
                // Policy and settings may change while an earlier display recovers; preserve their latest values.
                request = desired;
            }
            if (!_sessions.TryGetValue(target.DeviceId, out display!))
                _sessions[target.DeviceId] = display = new DisplaySession();
            // A recovery must not override a more recent user selection that is still preparing its first frame.
            if (topologyVersion.HasValue && display.Pending is not null) return;
            previousPending = display.Pending;
            display.Pending = pending;
            request = request with { Target = target, FramesPerSecond = _frameCap ?? request.FramesPerSecond };
            display.PendingRequest = request;
        }
        Cancel(previousPending);
        IWallpaperSession? next = null;
        try
        {
            next = await _factory(request, pending.Token);
            IWallpaperSession? previous;
            lock (_sync)
            {
                pending.Token.ThrowIfCancellationRequested();
                if (_disposed || !_sessions.TryGetValue(target.DeviceId, out var current) ||
                    !ReferenceEquals(current, display) || !ReferenceEquals(display.Pending, pending))
                    throw new OperationCanceledException(pending.Token);
                request = display.PendingRequest!;
                if (request.Settings is not null) next.UpdateVisualizerSettings(request.Settings);
                next.SetFrameCap(request.FramesPerSecond);
                next.SetAudioEnabled(_audioEnabled);
                var paused = _globallyPaused || _pausedDisplays.Contains(target.DeviceId);
                if (paused) next.Pause();
                // Preparation and reveal must both succeed before the good session or saved assignment is replaced.
                next.Show();
                pending.Token.ThrowIfCancellationRequested();
                if (_disposed || !_sessions.TryGetValue(target.DeviceId, out current) ||
                    !ReferenceEquals(current, display) || !ReferenceEquals(display.Pending, pending))
                    throw new OperationCanceledException(pending.Token);
                previous = display.Session;
                display.Session = next;
                display.Request = request;
                display.Paused = paused;
                display.Pending = null;
                display.PendingRequest = null;
                _desired[target.DeviceId] = request;
                _assignmentVersions[target.DeviceId] = ++_nextAssignmentVersion;
                next = null;
            }
            DisposeSession(previous);
            StateChanged?.Invoke();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(display.Pending, pending))
                {
                    display.Pending = null;
                    display.PendingRequest = null;
                    if (display.Session is null && _sessions.TryGetValue(target.DeviceId, out var current) && ReferenceEquals(current, display))
                        _sessions.Remove(target.DeviceId);
                }
            }
            DisposeSession(next);
        }
    }

    public void Stop(string deviceId)
    {
        DisplaySession? removed;
        bool changed;
        lock (_sync)
        {
            changed = _desired.Remove(deviceId);
            _assignmentVersions.Remove(deviceId);
            changed |= _sessions.Remove(deviceId, out removed);
        }
        Cancel(removed?.Pending);
        DisposeSession(removed?.Session);
        if (changed) StateChanged?.Invoke();
    }

    public void Stop()
    {
        DisplaySession[] removed;
        bool changed;
        lock (_sync)
        {
            removed = _sessions.Values.ToArray();
            changed = removed.Length > 0 || _desired.Count > 0;
            _sessions.Clear();
            _desired.Clear();
            _assignmentVersions.Clear();
            _topologyVersion++;
        }
        foreach (var display in removed)
        {
            Cancel(display.Pending);
            DisposeSession(display.Session);
        }
        if (changed) StateChanged?.Invoke();
    }

    public void Pause()
    {
        lock (_sync) { _globallyPaused = true; ApplyPauseState(); }
    }

    public void Resume()
    {
        lock (_sync) { _globallyPaused = false; ApplyPauseState(); }
    }

    public void SetPausedDisplays(IReadOnlyCollection<string> deviceIds)
    {
        lock (_sync)
        {
            _pausedDisplays = new HashSet<string>(deviceIds, StringComparer.OrdinalIgnoreCase);
            ApplyPauseState();
        }
    }

    private void ApplyPauseState()
    {
        foreach (var (deviceId, display) in _sessions)
        {
            var paused = _globallyPaused || _pausedDisplays.Contains(deviceId);
            if (display.Session is null || display.Paused == paused) continue;
            if (paused) display.Session.Pause(); else display.Session.Resume();
            display.Paused = paused;
        }
        // These policy updates deliberately do not raise StateChanged: the UI applies policy from that event.
    }

    public void SetFrameCap(int framesPerSecond)
    {
        lock (_sync)
        {
            if (_frameCap == framesPerSecond) return;
            _frameCap = framesPerSecond;
            foreach (var display in _sessions.Values)
            {
                if (display.Request is not null) display.Request = display.Request with { FramesPerSecond = framesPerSecond };
                if (display.PendingRequest is not null) display.PendingRequest = display.PendingRequest with { FramesPerSecond = framesPerSecond };
                display.Session?.SetFrameCap(framesPerSecond);
            }
            foreach (var id in _desired.Keys.ToArray()) _desired[id] = _desired[id] with { FramesPerSecond = framesPerSecond };
        }
    }

    public void SetAudioEnabled(bool enabled)
    {
        lock (_sync)
        {
            if (_audioEnabled == enabled) return;
            _audioEnabled = enabled;
            foreach (var display in _sessions.Values) display.Session?.SetAudioEnabled(enabled);
        }
    }

    public void UpdateVisualizerSettings(string deviceId, VisualizerSettings settings)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(deviceId, out var display))
            {
                if (display.Request is not null) display.Request = display.Request with { Settings = settings };
                if (display.PendingRequest is not null) display.PendingRequest = display.PendingRequest with { Settings = settings };
                display.Session?.UpdateVisualizerSettings(settings);
            }
            if (_desired.TryGetValue(deviceId, out var request)) _desired[deviceId] = request with { Settings = settings };
        }
    }

    public async Task ReconcileAsync(IReadOnlyList<DesktopWorker.WallpaperTarget> connected, bool force = false)
    {
        foreach (var target in connected) ValidateTarget(target);
        var targets = connected.ToDictionary(target => target.DeviceId, StringComparer.OrdinalIgnoreCase);
        var removed = new List<DisplaySession>();
        var cancelled = new List<CancellationTokenSource>();
        (string DeviceId, WallpaperRequest Request, long Revision)[] wanted;
        long topologyVersion;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            topologyVersion = ++_topologyVersion;
            foreach (var (deviceId, display) in _sessions.ToArray())
            {
                if (!targets.TryGetValue(deviceId, out var target))
                {
                    _sessions.Remove(deviceId);
                    removed.Add(display);
                }
                else if (display.Pending is not null && display.PendingRequest?.Target is { } pendingTarget && !SamePlacement(pendingTarget, target))
                {
                    cancelled.Add(display.Pending);
                    display.Pending = null;
                    display.PendingRequest = null;
                }
            }
            wanted = _desired.Select(pair => (pair.Key, pair.Value, _assignmentVersions[pair.Key])).ToArray();
        }
        foreach (var pending in cancelled) Cancel(pending);
        foreach (var display in removed)
        {
            Cancel(display.Pending);
            DisposeSession(display.Session);
        }
        if (removed.Count > 0) StateChanged?.Invoke();
        List<Exception>? failures = null;
        foreach (var (deviceId, request, revision) in wanted)
        {
            if (!targets.TryGetValue(deviceId, out var target)) continue;
            lock (_sync)
            {
                if (_disposed || topologyVersion != _topologyVersion) return;
                if (_sessions.TryGetValue(deviceId, out var display) && (display.Pending is not null ||
                    (!force && display.Session?.IsHealthy == true && display.Request?.Target is { } activeTarget && SamePlacement(activeTarget, target)))) continue;
            }
            try { await StartCoreAsync(request, target, revision, topologyVersion); }
            catch (OperationCanceledException) { /* Stop, topology changes, and a newer selection supersede recovery. */ }
            catch (ObjectDisposedException) when (_disposed) { return; }
            catch (Exception exception)
            {
                (failures ??= []).Add(new InvalidOperationException($"Wallpaper recovery failed for display '{deviceId}'.", exception));
            }
        }
        if (failures is not null) throw new AggregateException("Some displays could not restore their wallpaper.", failures);
    }

    private static bool SamePlacement(DesktopWorker.WallpaperTarget left, DesktopWorker.WallpaperTarget right)
        => left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height &&
           left.DpiX == right.DpiX && left.DpiY == right.DpiY;

    private static void ValidateTarget(DesktopWorker.WallpaperTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.DeviceId)) throw new ArgumentException("A stable display identity is required.", nameof(target));
        if (target.Width <= 0 || target.Height <= 0) throw new ArgumentOutOfRangeException(nameof(target), "Display bounds must have a positive size.");
    }

    private static void Cancel(CancellationTokenSource? pending)
    {
        try { pending?.Cancel(); }
        catch (ObjectDisposedException) { /* The completed start already disposed its token source. */ }
        catch (AggregateException exception) { AppLog.WriteException("Display wallpaper cancellation failed", exception); }
    }

    private static void DisposeSession(IWallpaperSession? session)
    {
        try { session?.Dispose(); }
        catch (Exception exception) { AppLog.WriteException("Display wallpaper cleanup failed", exception); }
    }

    public void Dispose()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        Stop();
    }
}
