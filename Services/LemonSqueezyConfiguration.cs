using System.Reflection;

namespace AnimatedWallPaper.Services;

internal sealed record LemonSqueezyConfiguration(int StoreId, int ProductId, int VariantId, string CheckoutUrl)
{
    public string Scope => $"{StoreId}/{ProductId}/{VariantId}";
    public bool IsConfigured => StoreId > 0 && ProductId > 0 && VariantId > 0 && IsCheckoutUrl(CheckoutUrl);
    public static bool IsCheckoutUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host.EndsWith(".lemonsqueezy.com", StringComparison.OrdinalIgnoreCase)
        && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"\A/(checkout/)?buy/[a-zA-Z0-9-]+\z")
        && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    public static LemonSqueezyConfiguration Current { get; } = Load();
    private static LemonSqueezyConfiguration Load()
    {
        var values = typeof(LemonSqueezyConfiguration).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(x => x.Key, x => x.Value ?? "");
        int Id(string key) => values.TryGetValue(key, out var value) && int.TryParse(value, out var id) ? id : 0;
        return new(Id("LemonSqueezyStoreId"), Id("LemonSqueezyProductId"), Id("LemonSqueezyVariantId"),
            values.GetValueOrDefault("LemonSqueezyCheckoutUrl", ""));
    }
}
