namespace AnimatedWallPaper.Services;

internal enum AppLicenseKind { Unmanaged, Checking, Trial, Owned, Expired, NotOwned, Unavailable }
internal enum AppPurchaseResult { Purchased, AlreadyOwned, Cancelled, NetworkError, Error, CheckoutOpened }

internal sealed record AppLicenseSnapshot(AppLicenseKind Kind, DateTimeOffset? ExpiresAt = null)
{
    public static AppLicenseSnapshot FromStore(bool active, bool trial, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (trial) return new(active && expiresAt > now ? AppLicenseKind.Trial : AppLicenseKind.Expired, expiresAt);
        return new(active ? AppLicenseKind.Owned : AppLicenseKind.NotOwned);
    }
    public AppLicenseSnapshot At(DateTimeOffset now)
        => Kind == AppLicenseKind.Trial && (ExpiresAt is null || ExpiresAt <= now) ? this with { Kind = AppLicenseKind.Expired }
            : Kind == AppLicenseKind.Owned && ExpiresAt <= now ? this with { Kind = AppLicenseKind.Unavailable } : this;
    public bool CanPlay(DateTimeOffset now) => At(now).Kind is AppLicenseKind.Unmanaged or AppLicenseKind.Owned or AppLicenseKind.Trial;
    public int DaysRemaining(DateTimeOffset now) => Kind == AppLicenseKind.Trial && ExpiresAt is { } end
        ? (int)Math.Max(0, Math.Ceiling((end - now).TotalDays)) : 0;
}

internal interface IAppLicenseProvider : IDisposable
{
    bool IsStoreManaged { get; }
    bool IsManaged => IsStoreManaged;
    event Action? LicenseChanged;
    void Initialize(IntPtr owner);
    Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken cancellationToken);
    Task<string?> GetPriceAsync(CancellationToken cancellationToken);
    Task<AppPurchaseResult> PurchaseAsync(CancellationToken cancellationToken);
}

// Providers own persistence and verification; the service enforces their deadlines even in the tray.
internal sealed class AppLicenseService : IDisposable
{
    private readonly IAppLicenseProvider _provider;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTimeOffset _startedAt;
    private readonly long _startedTimestamp;
    private Task? _refresh;
    private Task? _priceRefresh;
    private bool _disposed;
    private int _displayedDays;
    public AppLicenseSnapshot Snapshot { get; private set; }
    public string? FormattedPrice { get; private set; }
    public string? Notice { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsPurchasing { get; private set; }
    public bool IsStoreManaged => _provider.IsStoreManaged;
    public bool IsManaged => _provider.IsManaged;
    public bool UsesLicenseKey => _provider is IKeyLicenseProvider;
    public bool HasActivation => _provider is IKeyLicenseProvider { HasActivation: true };
    public bool CanPurchase => _provider is not IKeyLicenseProvider keys || keys.CanPurchase;
    public bool IsManagingKey { get; private set; }
    public bool CanPlay => Snapshot.CanPlay(Now);
    public int DaysRemaining => Snapshot.DaysRemaining(Now);
    // Moving the Windows clock backwards cannot lengthen a running trial.
    private DateTimeOffset Now => new(Math.Max(_clock.GetUtcNow().UtcTicks,
        (_startedAt + _clock.GetElapsedTime(_startedTimestamp)).UtcTicks), TimeSpan.Zero);
    public event Action? Changed;
    public event Action? RefreshRequested;

    public AppLicenseService(IAppLicenseProvider provider, TimeProvider? clock = null)
    {
        _provider = provider; _clock = clock ?? TimeProvider.System;
        _startedAt = _clock.GetUtcNow(); _startedTimestamp = _clock.GetTimestamp();
        Snapshot = new(provider.IsManaged ? AppLicenseKind.Checking : AppLicenseKind.Unmanaged);
        _provider.LicenseChanged += OnLicenseChanged;
    }
    public void Initialize(IntPtr owner)
    {
        if (_disposed || !IsManaged) return;
        try { _provider.Initialize(owner); }
        catch (Exception exception)
        {
            AppLog.WriteException("Store initialization", exception);
            Snapshot = new(AppLicenseKind.Unavailable); Changed?.Invoke();
        }
    }
    private void OnLicenseChanged() => RefreshRequested?.Invoke();
    public void EvaluateTime()
    {
        if (_disposed) return;
        var current = Snapshot.At(Now);
        var days = current.DaysRemaining(Now);
        if (current != Snapshot || days != _displayedDays)
        {
            Snapshot = current; _displayedDays = days; Changed?.Invoke();
        }
    }
    public Task RefreshAsync(bool force = false)
    {
        if (_disposed || !IsManaged) return Task.CompletedTask;
        if (_refresh is { IsCompleted: false }) return force ? RefreshAfterPendingAsync(_refresh) : _refresh;
        return _refresh = RefreshCoreAsync();
    }
    private async Task RefreshAfterPendingAsync(Task pending)
    {
        await pending;
        await RefreshAsync();
    }
    private async Task RefreshCoreAsync()
    {
        IsRefreshing = true; Changed?.Invoke();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var license = await _provider.GetLicenseAsync(timeout.Token).WaitAsync(timeout.Token);
            if (_disposed) return;
            Snapshot = license.At(Now); Notice = null;
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            LogFailure("License refresh", exception);
            Snapshot = Snapshot.At(Now);
            if (!Snapshot.CanPlay(Now) && Snapshot.Kind != AppLicenseKind.Expired) Snapshot = new(AppLicenseKind.Unavailable);
            if (UsesLicenseKey) Snapshot = new(AppLicenseKind.Unavailable);
            Notice = UsesLicenseKey ? SafeMessage(exception) : "Could not reach Microsoft Store. Check your connection and try again.";
        }
        finally
        {
            IsRefreshing = false;
            if (!_disposed) { _displayedDays = DaysRemaining; Changed?.Invoke(); }
        }
    }
    public Task RefreshPriceAsync()
    {
        if (_disposed || !IsManaged) return Task.CompletedTask;
        if (_priceRefresh is { IsCompleted: false }) return _priceRefresh;
        return _priceRefresh = RefreshPriceCoreAsync();
    }
    private async Task RefreshPriceCoreAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var price = await _provider.GetPriceAsync(timeout.Token).WaitAsync(timeout.Token);
            if (!_disposed) { FormattedPrice = string.IsNullOrWhiteSpace(price) ? null : price; Changed?.Invoke(); }
        }
        catch (Exception exception) { if (!_disposed) AppLog.WriteException("Store price unavailable", exception); }
    }
    public async Task PurchaseAsync()
    {
        if (_disposed || !IsManaged || IsPurchasing || IsManagingKey || Snapshot.Kind == AppLicenseKind.Owned) return;
        IsPurchasing = true; Notice = null; Changed?.Invoke();
        try
        {
            var result = await _provider.PurchaseAsync(_lifetime.Token);
            if (_disposed) return;
            // A successful dialog alone does not unlock the app: re-read the actual license.
            await RefreshAsync(force: true);
            if (_disposed) return;
            Notice = result switch {
                AppPurchaseResult.CheckoutOpened => "Lemon Squeezy checkout opened in your browser. After purchasing, enter the license key from your receipt. Opening checkout does not activate HYPNIX.",
                AppPurchaseResult.Purchased or AppPurchaseResult.AlreadyOwned when Snapshot.Kind == AppLicenseKind.Owned => "Thank you. HYPNIX is yours — no subscription.",
                AppPurchaseResult.Purchased or AppPurchaseResult.AlreadyOwned => "Your purchase is being confirmed. Choose Check license to refresh.",
                AppPurchaseResult.Cancelled => "Purchase cancelled. No changes were made to your license.",
                AppPurchaseResult.NetworkError => "Microsoft Store could not connect. Please try again when you are online.",
                _ => "The purchase could not be completed. Please try again in Microsoft Store."
            };
        }
        catch (Exception exception)
        {
            if (_disposed) return;
            LogFailure("Purchase", exception);
            Notice = UsesLicenseKey ? SafeMessage(exception) : "The purchase could not be completed. Check your connection and try again.";
        }
        finally { IsPurchasing = false; if (!_disposed) Changed?.Invoke(); }
    }
    public Task ActivateAsync(string key) => ManageKeyAsync(key);
    public Task DeactivateAsync() => ManageKeyAsync(null);
    private async Task ManageKeyAsync(string? key)
    {
        if (_disposed || _provider is not IKeyLicenseProvider provider || IsManagingKey || IsPurchasing) return;
        IsManagingKey = true; Notice = null; Changed?.Invoke();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            if (key is null) await provider.DeactivateAsync(timeout.Token);
            else await provider.ActivateAsync(key, timeout.Token);
            if (_disposed) return;
            await RefreshAsync(force: true);
            if (_disposed) return;
            Notice = key is null ? "This PC has been deactivated. Your settings are saved."
                : Snapshot.Kind == AppLicenseKind.Owned ? "License activated. HYPNIX is yours — no subscription."
                : "Activation was received. Check your license to confirm access.";
        }
        catch (Exception exception)
        {
            if (!_disposed) { LogFailure("License operation", exception); Notice = SafeMessage(exception); }
        }
        finally { IsManagingKey = false; if (!_disposed) Changed?.Invoke(); }
    }
    private static string SafeMessage(Exception exception) => exception is LicenseOperationException
        ? exception.Message : "Could not complete the license request. Check your connection and try again, or contact hello@mablook.com.";
    private void LogFailure(string context, Exception exception)
    {
        if (UsesLicenseKey) AppLog.Write($"{context}: {exception.GetType().Name}");
        else AppLog.WriteException(context, exception);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _provider.LicenseChanged -= OnLicenseChanged;
        _lifetime.Cancel(); _provider.Dispose(); _lifetime.Dispose();
    }
}
