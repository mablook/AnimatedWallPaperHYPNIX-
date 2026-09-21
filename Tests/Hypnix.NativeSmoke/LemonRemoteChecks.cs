using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using AnimatedWallPaper.Services;

// Explicit opt-in integration test: uses a test purchase, not a merchant API key.
internal static class LemonRemoteChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }

    public static async Task RunAsync(string output)
    {
        if (XDocument.Load("packaging/Commerce.props").Descendants("LemonSqueezyMode").Single().Value != "Test")
            throw new InvalidOperationException("Remote checks require explicit Test configuration.");
        var keyFile = Environment.GetEnvironmentVariable("HYPNIX_TEST_LICENSE_FILE")
            ?? throw new InvalidOperationException("Set HYPNIX_TEST_LICENSE_FILE to the private test receipt key file.");
        var key = File.ReadAllText(keyFile).Trim();
        var config = LemonSqueezyConfiguration.Current;
        Check(config.IsConfigured, "Compiled commerce configuration is complete");
        var statePath = Path.Combine(output, "remote-license.dat");
        var store = new LicenseStateStore(statePath);
        LemonSqueezyLicenseProvider Provider(LemonSqueezyConfiguration settings) => new(settings,
            new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) }), store);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var token = timeout.Token;
        using var provider = Provider(config);
        try
        {
            using (var wrongProduct = Provider(config with { ProductId = int.MaxValue }))
            {
                var rejected = false;
                try { await wrongProduct.ActivateAsync(key, token); }
                catch (LicenseOperationException) { rejected = true; }
                Check(rejected && !wrongProduct.HasActivation, "Wrong product identity rejected before activation");
            }
            await provider.ActivateAsync(key, token);
            Check((await provider.GetLicenseAsync(token)).Kind == AppLicenseKind.Owned, "Issued key activates and validates against Lemon Squeezy");
            var originalInstance = store.Load()!.InstanceId;
            var trialStart = store.Load()!.TrialStartedAt;
            await provider.ActivateAsync(key, token);
            Check(store.Load()!.InstanceId == originalInstance, "Repeated activation reuses the same instance");
            using (var restarted = Provider(config))
                Check((await restarted.GetLicenseAsync(token)).Kind == AppLicenseKind.Owned,
                    "New provider restores encrypted state and validates persisted instance");
            await provider.DeactivateAsync(token);
            Check(!provider.HasActivation && store.Load()!.Key is null, "Deactivation releases instance and clears local key");
            Check(store.Load()!.TrialStartedAt == trialStart, "Deactivation preserves original trial start");
            await provider.ActivateAsync(key, token);
            Check((await provider.GetLicenseAsync(token)).Kind == AppLicenseKind.Owned, "Key can activate again after releasing its slot");
        }
        finally
        {
            // Preserve encrypted recovery state if the network prevents releasing a slot.
            if (provider.HasActivation) await provider.DeactivateAsync(CancellationToken.None);
            Check(!provider.HasActivation, "Test activation cleaned up");
        }
    }
}
