using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class AppLicenseTests
{
    [Fact]
    public void StoreFlagsDistinguishPaidTrialExpiredAndMissingLicense()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(AppLicenseKind.Owned, AppLicenseSnapshot.FromStore(true, false, now, now).Kind);
        Assert.Equal(AppLicenseKind.NotOwned, AppLicenseSnapshot.FromStore(false, false, now, now).Kind);
        Assert.Equal(AppLicenseKind.Trial, AppLicenseSnapshot.FromStore(true, true, now.AddDays(15), now).Kind);
        Assert.Equal(AppLicenseKind.Expired, AppLicenseSnapshot.FromStore(false, true, now.AddDays(15), now).Kind);
        Assert.Equal(AppLicenseKind.Expired, AppLicenseSnapshot.FromStore(true, true, now, now).Kind);
    }
    [Fact]
    public async Task TrialExpiresWithoutNetworkRefreshAndRoundsRemainingDaysUp()
    {
        var clock = new Clock(); var provider = new Provider { Snapshot = new(AppLicenseKind.Trial, clock.GetUtcNow().AddDays(15)) };
        using var service = new AppLicenseService(provider, clock);
        await service.RefreshAsync(); Assert.Equal(15, service.DaysRemaining); Assert.True(service.CanPlay);
        clock.Advance(TimeSpan.FromDays(14.5)); service.EvaluateTime();
        Assert.Equal(1, service.DaysRemaining);
        clock.Advance(TimeSpan.FromHours(12)); service.EvaluateTime();
        Assert.Equal(AppLicenseKind.Expired, service.Snapshot.Kind); Assert.False(service.CanPlay);
        Assert.Equal(1, provider.Reads);
    }
    [Fact]
    public async Task MovingClockBackDoesNotExtendRunningTrial()
    {
        var clock = new Clock(); var provider = new Provider { Snapshot = new(AppLicenseKind.Trial, clock.GetUtcNow().AddMinutes(1)) };
        using var service = new AppLicenseService(provider, clock); await service.RefreshAsync();
        clock.Advance(TimeSpan.FromMinutes(2)); clock.Utc = clock.Utc.AddDays(-10); service.EvaluateTime();
        Assert.False(service.CanPlay); Assert.Equal(AppLicenseKind.Expired, service.Snapshot.Kind);
    }
    [Fact]
    public async Task MissingLicenseCannotStartAndFailureNeverGrantsAccess()
    {
        var provider = new Provider { Failure = true };
        using var service = new AppLicenseService(provider);
        Assert.False(service.CanPlay); await service.RefreshAsync();
        Assert.Equal(AppLicenseKind.Unavailable, service.Snapshot.Kind); Assert.False(service.CanPlay);
    }
    [Fact]
    public async Task OfflineTrialUsesKnownDeadlineThenExpires()
    {
        var clock = new Clock(); var provider = new Provider { Snapshot = new(AppLicenseKind.Trial, clock.GetUtcNow().AddSeconds(30)) };
        using var service = new AppLicenseService(provider, clock); await service.RefreshAsync();
        provider.Failure = true; await service.RefreshAsync(); Assert.True(service.CanPlay);
        clock.Advance(TimeSpan.FromSeconds(31)); await service.RefreshAsync();
        Assert.Equal(AppLicenseKind.Expired, service.Snapshot.Kind); Assert.False(service.CanPlay);
    }
    [Fact]
    public async Task PaidUserRetainsKnownLicenseDuringTransientFailure()
    {
        var provider = new Provider { Snapshot = new(AppLicenseKind.Owned) };
        using var service = new AppLicenseService(provider); await service.RefreshAsync();
        provider.Failure = true; await service.RefreshAsync(); Assert.True(service.CanPlay);
        provider.Failure = false; provider.Snapshot = new(AppLicenseKind.NotOwned); await service.RefreshAsync();
        Assert.False(service.CanPlay); // An explicit revocation overrides the previous license.
    }
    [Fact]
    public async Task SuccessfulPurchaseNeedsConfirmedLicense()
    {
        var provider = new Provider { Snapshot = new(AppLicenseKind.Expired), PurchaseResult = AppPurchaseResult.Purchased };
        using var service = new AppLicenseService(provider); await service.RefreshAsync(); await service.PurchaseAsync();
        Assert.False(service.CanPlay); Assert.Contains("confirmed", service.Notice);
        provider.PurchasedSnapshot = new(AppLicenseKind.Owned); await service.PurchaseAsync();
        Assert.True(service.CanPlay); Assert.Equal(AppLicenseKind.Owned, service.Snapshot.Kind);
    }
    [Fact]
    public async Task CancellationDoesNotDestroyTrialOrUnlockExpiredLicense()
    {
        var provider = new Provider { Snapshot = new(AppLicenseKind.Trial, DateTimeOffset.UtcNow.AddDays(2)), PurchaseResult = AppPurchaseResult.Cancelled };
        using var service = new AppLicenseService(provider); await service.RefreshAsync(); await service.PurchaseAsync();
        Assert.Equal(AppLicenseKind.Trial, service.Snapshot.Kind); Assert.True(service.CanPlay);
        provider.Snapshot = new(AppLicenseKind.Expired); await service.RefreshAsync(); await service.PurchaseAsync();
        Assert.False(service.CanPlay); Assert.Contains("cancelled", service.Notice);
    }
    [Fact]
    public async Task PurchaseNetworkErrorLeavesLicenseLockedAndCanRetry()
    {
        var provider = new Provider { PurchaseResult = AppPurchaseResult.NetworkError };
        using var service = new AppLicenseService(provider); await service.RefreshAsync(); await service.PurchaseAsync();
        Assert.False(service.IsPurchasing); Assert.False(service.CanPlay); Assert.Contains("connect", service.Notice);
        provider.PurchaseResult = AppPurchaseResult.AlreadyOwned; provider.PurchasedSnapshot = new(AppLicenseKind.Owned);
        await service.PurchaseAsync(); Assert.True(service.CanPlay);
    }
    [Fact]
    public async Task ConcurrentRefreshesShareOneRequestAndForcedRefreshRechecks()
    {
        var provider = new Provider { PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = new AppLicenseService(provider);
        var first = service.RefreshAsync(); var second = service.RefreshAsync(); var forced = service.RefreshAsync(force: true);
        Assert.Equal(1, provider.Reads); Assert.Same(first, second);
        provider.PendingRead.SetResult(new(AppLicenseKind.Trial, DateTimeOffset.UtcNow.AddDays(1)));
        await Task.WhenAll(first, second, forced); Assert.Equal(2, provider.Reads);
    }
    [Fact]
    public async Task PurchaseDoubleClickDoesNotOpenMultipleDialogs()
    {
        var provider = new Provider { PendingPurchase = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var service = new AppLicenseService(provider); await service.RefreshAsync();
        var first = service.PurchaseAsync(); await service.PurchaseAsync(); Assert.Equal(1, provider.Purchases);
        provider.PendingPurchase.SetResult(AppPurchaseResult.Cancelled); await first;
        Assert.False(service.IsPurchasing);
    }
    [Fact]
    public async Task PriceIsLocalizedByStoreAndFailureDoesNotBlockValidLicense()
    {
        var provider = new Provider { Snapshot = new(AppLicenseKind.Owned), Price = "R$ 29,90" };
        using var service = new AppLicenseService(provider); await service.RefreshAsync(); await service.RefreshPriceAsync();
        Assert.Equal("R$ 29,90", service.FormattedPrice);
        provider.Failure = true; await service.RefreshPriceAsync(); Assert.True(service.CanPlay);
    }
    [Fact]
    public async Task UnpackagedChannelDoesNotQueryStoreOrStartLocalTrial()
    {
        var provider = new Provider { IsStoreManaged = false };
        using var service = new AppLicenseService(provider); await service.RefreshAsync(); await service.PurchaseAsync();
        Assert.True(service.CanPlay); Assert.Equal(AppLicenseKind.Unmanaged, service.Snapshot.Kind);
        Assert.Equal(0, provider.Reads); Assert.Equal(0, provider.Purchases);
    }
    [Fact]
    public async Task DisposalCancelsPendingReadAndDisconnectsLicenseNotifications()
    {
        var provider = new Provider { PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new AppLicenseService(provider); var notifications = 0;
        service.RefreshRequested += () => notifications++;
        provider.Notify(); Assert.Equal(1, notifications);
        var pending = service.RefreshAsync(); service.Dispose(); await pending;
        provider.Notify(); Assert.Equal(1, notifications); Assert.True(provider.Disposed);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan value) { Utc += value; _ticks += value.Ticks; }
    }
    private sealed class Provider : IAppLicenseProvider
    {
        public bool IsStoreManaged { get; init; } = true;
        public event Action? LicenseChanged;
        public AppLicenseSnapshot Snapshot = new(AppLicenseKind.NotOwned);
        public AppLicenseSnapshot? PurchasedSnapshot;
        public AppPurchaseResult PurchaseResult = AppPurchaseResult.Cancelled;
        public TaskCompletionSource<AppLicenseSnapshot>? PendingRead;
        public TaskCompletionSource<AppPurchaseResult>? PendingPurchase;
        public bool Failure, Disposed;
        public int Reads, Purchases;
        public string? Price;
        public void Initialize(IntPtr owner) { }
        public void Notify() => LicenseChanged?.Invoke();
        public Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken cancellationToken)
        {
            Reads++;
            if (Failure) throw new IOException("Offline");
            return PendingRead?.Task ?? Task.FromResult(Snapshot);
        }
        public Task<string?> GetPriceAsync(CancellationToken cancellationToken)
            => Failure ? Task.FromException<string?>(new IOException("Offline")) : Task.FromResult(Price);
        public Task<AppPurchaseResult> PurchaseAsync(CancellationToken cancellationToken)
        {
            Purchases++;
            if (PurchasedSnapshot is { } snapshot) Snapshot = snapshot;
            return PendingPurchase?.Task ?? Task.FromResult(PurchaseResult);
        }
        public void Dispose() => Disposed = true;
    }
}
