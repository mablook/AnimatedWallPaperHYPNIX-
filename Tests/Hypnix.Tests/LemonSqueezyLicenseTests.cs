using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AnimatedWallPaper.Services;

namespace Hypnix.Tests;

public sealed class LemonSqueezyLicenseTests
{
    private static readonly LemonSqueezyConfiguration Config = new(10, 20, 30, "https://hypnix.lemonsqueezy.com/buy/test-product");
    private const string Key = "test-license-key";
    private sealed class RawApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
    [Theory]
    [InlineData("https://hypnix.lemonsqueezy.com/buy/test-product")]
    [InlineData("https://mablook.lemonsqueezy.com/checkout/buy/116a9d7d-9a17-4fd3-97d6-8769347438c5")]
    public void HostedCheckoutUrlsAreAccepted(string url) => Assert.True(LemonSqueezyConfiguration.IsCheckoutUrl(url));

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task HttpErrorsCannotClaimSuccessfulActivation(HttpStatusCode status)
    {
        using var client = new LemonSqueezyLicenseClient(new HttpClient(new RawApi(status, "{\"activated\":true,\"valid\":true}")));
        var result = await client.SendAsync("activate", Key, "instance", default);
        Assert.False(result.Activated); Assert.False(result.Valid);
    }
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RateLimitsAndServerOutagesAreTransient(HttpStatusCode status)
    {
        using var client = new LemonSqueezyLicenseClient(new HttpClient(new RawApi(status, "")));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync("validate", Key, null, default));
    }
    [Fact]
    public async Task MalformedResponseDoesNotLeakProviderBodyIntoError()
    {
        using var client = new LemonSqueezyLicenseClient(new HttpClient(new RawApi(HttpStatusCode.OK, "private@email.example")));
        var error = await Assert.ThrowsAsync<LicenseOperationException>(() => client.SendAsync("validate", Key, null, default));
        Assert.DoesNotContain("private@", error.ToString());
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Store : ILicenseStateStore
    {
        public LicenseState? State;
        public LicenseState? Load() => State;
        public void Save(LicenseState state) => State = state;
    }
    private sealed class Api : HttpMessageHandler
    {
        public int Activations, Deactivations, Validations;
        public bool Offline, Revoked, Full, WrongProduct, WrongInstance;
        public DateTimeOffset? Expiry;
        public string? LastForm;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("api.lemonsqueezy.com", request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/json");
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            LastForm = await request.Content.ReadAsStringAsync(token);
            if (Offline) throw new HttpRequestException("Simulated network failure");
            var action = request.RequestUri.Segments.Last();
            if (action == "activate") Activations++;
            if (action == "deactivate") Deactivations++;
            if (action == "validate") Validations++;
            var body = JsonSerializer.Serialize(new {
                valid = !Revoked, activated = !Revoked && !Full, deactivated = true,
                license_key = new { key = Key, status = Revoked ? "disabled" : "active", expires_at = Expiry },
                instance = new { id = WrongInstance ? "another-pc" : "instance-1" },
                meta = new { store_id = 10, product_id = WrongProduct ? 999 : 20, variant_id = 30 }
            });
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    private static LemonSqueezyLicenseProvider Provider(Api api, Store store, Clock clock, Action<string>? checkout = null)
        => new(Config, new(new HttpClient(api, disposeHandler: false)), store, clock, checkout);

    [Fact]
    public async Task TrialPersistsAcrossRestartsAndExpiresAtFifteenDays()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using (var p = Provider(api, store, clock)) Assert.Equal(AppLicenseKind.Trial, (await p.GetLicenseAsync(default)).Kind);
        clock.Now = clock.Now.AddDays(15);
        using var restart = Provider(api, store, clock);
        Assert.Equal(AppLicenseKind.Expired, (await restart.GetLicenseAsync(default)).Kind);
        Assert.Equal(0, api.Validations);
    }
    [Fact]
    public async Task ActivationIsBoundToProductAndReusesExistingInstance()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using var p = Provider(api, store, clock);
        await p.ActivateAsync(Key, default);
        Assert.Equal(AppLicenseKind.Owned, (await p.GetLicenseAsync(default)).Kind);
        Assert.Contains("instance_id=instance-1", api.LastForm);
        await p.ActivateAsync(Key, default);
        Assert.Equal(1, api.Activations);
        Assert.Equal("10/20/30", store.State!.Scope);
    }
    [Fact]
    public async Task WrongProductNeverConsumesActivation()
    {
        var api = new Api { WrongProduct = true };
        using var p = Provider(api, new(), new());
        await Assert.ThrowsAsync<LicenseOperationException>(() => p.ActivateAsync(Key, default));
        Assert.False(p.HasActivation); Assert.Equal(0, api.Activations);
    }
    [Fact]
    public async Task ActivationLimitDoesNotGrantPaidAccess()
    {
        var api = new Api { Full = true }; var store = new Store();
        using var p = Provider(api, store, new());
        await Assert.ThrowsAsync<LicenseOperationException>(() => p.ActivateAsync(Key, default));
        Assert.Null(store.State!.Key); Assert.Null(store.State.VerifiedUntil);
    }
    [Fact]
    public async Task OfflineGracePersistsAcrossRestartButNeverBeyondSevenDays()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using (var p = Provider(api, store, clock)) await p.ActivateAsync(Key, default);
        api.Offline = true; clock.Now = clock.Now.AddDays(6);
        using var restarted = Provider(api, store, clock);
        var snapshot = await restarted.GetLicenseAsync(default);
        Assert.True(snapshot.CanPlay(clock.Now));
        clock.Now = clock.Now.AddDays(1);
        Assert.False(snapshot.CanPlay(clock.Now)); // No refresh required for enforcement.
        Assert.Equal(AppLicenseKind.Unavailable, (await restarted.GetLicenseAsync(default)).Kind);
    }
    [Fact]
    public async Task RevocationOverridesOfflineCacheImmediately()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using var p = Provider(api, store, clock);
        await p.ActivateAsync(Key, default); api.Revoked = true;
        Assert.Equal(AppLicenseKind.NotOwned, (await p.GetLicenseAsync(default)).Kind);
        api.Offline = true;
        Assert.Equal(AppLicenseKind.Unavailable, (await p.GetLicenseAsync(default)).Kind);
        Assert.Null(store.State!.VerifiedUntil);
    }
    [Fact]
    public async Task InstanceMismatchCannotUnlock()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using var p = Provider(api, store, clock);
        await p.ActivateAsync(Key, default); api.WrongInstance = true;
        Assert.Equal(AppLicenseKind.NotOwned, (await p.GetLicenseAsync(default)).Kind);
    }
    [Fact]
    public async Task DeactivationReleasesInstanceAndDoesNotResetExpiredTrial()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using var p = Provider(api, store, clock);
        await p.GetLicenseAsync(default); clock.Now = clock.Now.AddDays(20);
        await p.ActivateAsync(Key, default); await p.DeactivateAsync(default);
        Assert.Equal(1, api.Deactivations); Assert.Null(store.State!.Key);
        Assert.Equal(AppLicenseKind.Expired, (await p.GetLicenseAsync(default)).Kind);
    }
    [Fact]
    public async Task FailedDeactivationPreservesActivationForRetry()
    {
        var api = new Api(); var store = new Store();
        using var p = Provider(api, store, new());
        await p.ActivateAsync(Key, default); api.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => p.DeactivateAsync(default));
        Assert.True(p.HasActivation); Assert.Equal("instance-1", store.State!.InstanceId);
    }
    [Fact]
    public async Task BackwardClockDoesNotStartNewTrialOrExtendGrace()
    {
        var api = new Api(); var store = new Store(); var clock = new Clock();
        using (var p = Provider(api, store, clock)) await p.GetLicenseAsync(default);
        clock.Now = clock.Now.AddDays(-1);
        using var restarted = Provider(api, store, clock);
        await Assert.ThrowsAsync<LicenseOperationException>(() => restarted.GetLicenseAsync(default));
    }
    [Fact]
    public async Task ExpiringKeyCapsOfflineDeadline()
    {
        var clock = new Clock(); var api = new Api { Expiry = clock.Now.AddHours(1) };
        using var p = Provider(api, new(), clock);
        await p.ActivateAsync(Key, default);
        var license = await p.GetLicenseAsync(default);
        Assert.False(license.CanPlay(clock.Now.AddHours(1)));
    }
    [Fact]
    public async Task OpeningCheckoutNeverUnlocksAndDoesNotSendTheKeyInTheUrl()
    {
        var clock = new Clock(); string? opened = null;
        using var service = new AppLicenseService(Provider(new(), new(), clock, url => opened = url), clock);
        await service.RefreshAsync(); clock.Now = clock.Now.AddDays(15); service.EvaluateTime();
        await service.PurchaseAsync();
        Assert.Equal(Config.CheckoutUrl, opened); Assert.False(service.CanPlay);
        Assert.Contains("Lemon Squeezy", service.Notice);
    }
    [Fact]
    public async Task MissingConfigurationCannotOpenCheckoutOrActivate()
    {
        using var p = new LemonSqueezyLicenseProvider(new(0, 0, 0, ""), new(new HttpClient(new Api())), new Store());
        Assert.True(p.IsManaged); Assert.False(p.CanPurchase);
        await Assert.ThrowsAsync<LicenseOperationException>(() => p.ActivateAsync(Key, default));
        await Assert.ThrowsAsync<LicenseOperationException>(() => p.PurchaseAsync(default));
    }
    [Theory]
    [InlineData("http://test.lemonsqueezy.com/buy/id")]
    [InlineData("https://lemonsqueezy.com.attacker.example/buy/id")]
    [InlineData("https://test.lemonsqueezy.com/buy/id?test=1")]
    [InlineData("https://user:pass@test.lemonsqueezy.com/buy/id")]
    [InlineData("https://test.lemonsqueezy.com/checkout/buy/")]
    [InlineData("https://test.lemonsqueezy.com/checkout/buy/id?test=1")]
    [InlineData("https://test.lemonsqueezy.com/checkout/buy/id#fragment")]
    [InlineData("https://test.lemonsqueezy.com:444/checkout/buy/id")]
    [InlineData("https://test.lemonsqueezy.com/checkout/buy/id/extra")]
    [InlineData("https://test.lemonsqueezy.com.attacker.example/checkout/buy/id")]
    public void UnsafeOrTestCheckoutUrlsAreRejected(string url) => Assert.False(LemonSqueezyConfiguration.IsCheckoutUrl(url));

    [Fact]
    public void PersistedLicenseIsEncryptedAndCorruptionIsNotAReset()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hypnix-license-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "license.dat");
        try
        {
            var store = new LicenseStateStore(path); var now = DateTimeOffset.UtcNow;
            var state = new LicenseState(now, now, "installation") { Key = Key, InstanceId = "instance-1" };
            store.Save(state); Assert.Equal(state, store.Load());
            Assert.DoesNotContain(Key, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            File.WriteAllText(path, "corrupt"); Assert.ThrowsAny<Exception>(() => store.Load());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
