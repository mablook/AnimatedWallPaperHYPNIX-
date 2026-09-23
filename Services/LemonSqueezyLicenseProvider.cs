using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace AnimatedWallPaper.Services;

internal interface IKeyLicenseProvider
{
    bool HasActivation { get; }
    bool CanPurchase { get; }
    Task ActivateAsync(string key, CancellationToken token);
    Task DeactivateAsync(CancellationToken token);
}

internal sealed class LemonSqueezyLicenseProvider : IAppLicenseProvider, IKeyLicenseProvider
{
    private readonly LemonSqueezyConfiguration _config;
    private readonly LemonSqueezyLicenseClient _client;
    private readonly ILicenseStateStore _store;
    private readonly TimeProvider _clock;
    private readonly Action<string> _openCheckout;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private LicenseState? _state;
    public bool IsStoreManaged => false;
    public bool IsManaged => true;
    public bool HasActivation => _state?.InstanceId is not null;
    public bool CanPurchase => _config.IsConfigured;
    public event Action? LicenseChanged { add { } remove { } }
    public void Initialize(IntPtr owner) { }
    public static LemonSqueezyLicenseProvider Create()
    {
        var config = LemonSqueezyConfiguration.Current;
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HYPNIX", "licensing");
        return new(config,
            new LemonSqueezyLicenseClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) }),
            new ScopedLicenseStateStore(
                new LicenseStateStore(Path.Combine(directory, $"license-{config.StoreId}-{config.ProductId}-{config.VariantId}.dat")),
                new LicenseStateStore(Path.Combine(directory, "license.dat")), config.Scope));
    }
    public LemonSqueezyLicenseProvider(LemonSqueezyConfiguration config, LemonSqueezyLicenseClient client,
        ILicenseStateStore store, TimeProvider? clock = null, Action<string>? openCheckout = null)
    {
        _config = config; _client = client; _store = store; _clock = clock ?? TimeProvider.System;
        _openCheckout = openCheckout ?? (url => { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); });
    }
    private LicenseState ReadState()
    {
        var now = _clock.GetUtcNow();
        _state ??= _store.Load() ?? new LicenseState(now, now, Guid.NewGuid().ToString("N"));
        if (_state.TrialStartedAt > _state.LastSeenAt || string.IsNullOrWhiteSpace(_state.InstallationId)
            || now < _state.LastSeenAt.AddMinutes(-5))
            throw new LicenseOperationException("Check your Windows date and time, then check your license again.");
        Save(_state with { LastSeenAt = now > _state.LastSeenAt ? now : _state.LastSeenAt });
        return _state;
    }
    private void Save(LicenseState state) { _store.Save(state); _state = state; }
    private void RequireConfiguration()
    {
        if (!_config.IsConfigured) throw new LicenseOperationException("License sales are not configured in this build. Contact hello@mablook.com.");
    }
    private bool Matches(LemonLicenseResponse response, string key, DateTimeOffset now)
        => response.Meta is { } meta && meta.StoreId == _config.StoreId && meta.ProductId == _config.ProductId
            && meta.VariantId == _config.VariantId && response.Key is { } license
            && string.Equals(license.Key, key, StringComparison.Ordinal)
            && license.Status is "active" or "inactive" && (license.ExpiresAt is null || license.ExpiresAt > now);
    private static AppLicenseSnapshot Trial(LicenseState state, DateTimeOffset now)
        => new AppLicenseSnapshot(AppLicenseKind.Trial, state.TrialStartedAt.AddDays(15)).At(now);
    private AppLicenseSnapshot Cached(LicenseState state, DateTimeOffset now)
        => state.Scope == _config.Scope && state.InstanceId is not null && state.VerifiedUntil > now
            ? new(AppLicenseKind.Owned, state.VerifiedUntil) : new(AppLicenseKind.Unavailable);
    public async Task<AppLicenseSnapshot> GetLicenseAsync(CancellationToken token)
    {
        await _mutex.WaitAsync(token);
        try
        {
            var state = ReadState(); var now = _clock.GetUtcNow();
            if (state.Key is null || state.InstanceId is null) return Trial(state, now);
            RequireConfiguration();
            try
            {
                var response = await _client.SendAsync("validate", state.Key, state.InstanceId, token);
                if (!response.Valid || !Matches(response, state.Key, now) || response.Instance?.Id != state.InstanceId)
                {
                    Save(state with { VerifiedUntil = null });
                    return new(AppLicenseKind.NotOwned);
                }
                Save(Verified(state, response, now));
                return Cached(_state!, now);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
            { return Cached(state, now); }
        }
        finally { _mutex.Release(); }
    }
    private LicenseState Verified(LicenseState state, LemonLicenseResponse response, DateTimeOffset now)
    {
        var deadline = now.AddDays(7);
        if (response.Key?.ExpiresAt is { } expiry && expiry < deadline) deadline = expiry;
        return state with { Scope = _config.Scope, VerifiedUntil = deadline };
    }
    public async Task ActivateAsync(string key, CancellationToken token)
    {
        key = key.Trim();
        if (key.Length is < 8 or > 256 || key.Any(char.IsControl))
            throw new LicenseOperationException("Enter the license key from your Lemon Squeezy receipt.");
        await _mutex.WaitAsync(token);
        try
        {
            RequireConfiguration(); var state = ReadState(); var now = _clock.GetUtcNow();
            if (state.InstanceId is not null && state.Key != key)
                throw new LicenseOperationException("Deactivate this PC before entering a different license key.");
            // Check product identity before consuming an activation slot.
            var check = await _client.SendAsync("validate", key, null, token);
            if (!check.Valid || !Matches(check, key, now))
                throw new LicenseOperationException("This key is invalid, expired, disabled, or belongs to another product.");
            if (state.InstanceId is not null)
            {
                var existing = await _client.SendAsync("validate", key, state.InstanceId, token);
                if (existing.Valid && Matches(existing, key, now) && existing.Instance?.Id == state.InstanceId)
                { Save(Verified(state, existing, now)); return; }
                // The stored instance was removed remotely. Re-activate, without extending the trial.
            }
            var response = await _client.SendAsync("activate", key, "HYPNIX-" + state.InstallationId, token);
            if (!response.Activated || !Matches(response, key, now) || string.IsNullOrWhiteSpace(response.Instance?.Id))
                throw new LicenseOperationException("Activation failed. Check your key and available device slots, or contact hello@mablook.com.");
            var activated = Verified(state with { Key = key, InstanceId = response.Instance.Id }, response, now);
            try { Save(activated); }
            catch
            {
                // Best-effort release if Windows could not persist the activation.
                try { await _client.SendAsync("deactivate", key, response.Instance.Id, token); } catch { }
                throw new LicenseOperationException("Windows could not save your activation. Contact support before retrying.");
            }
        }
        finally { _mutex.Release(); }
    }
    public async Task DeactivateAsync(CancellationToken token)
    {
        await _mutex.WaitAsync(token);
        try
        {
            RequireConfiguration(); var state = ReadState();
            if (state.Key is null || state.InstanceId is null) return;
            var response = await _client.SendAsync("deactivate", state.Key, state.InstanceId, token);
            if (!response.Deactivated) throw new LicenseOperationException("Could not deactivate this PC. Check your connection or contact hello@mablook.com.");
            Save(state with { Key = null, InstanceId = null, Scope = null, VerifiedUntil = null });
        }
        finally { _mutex.Release(); }
    }
    public Task<string?> GetPriceAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<AppPurchaseResult> PurchaseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireConfiguration();
        _openCheckout(_config.CheckoutUrl);
        return Task.FromResult(AppPurchaseResult.CheckoutOpened);
    }
    public void Dispose() => _client.Dispose();
}
