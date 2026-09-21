using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnimatedWallPaper.Services;

// Public license API only: no merchant credentials, customer email, or payment data.
internal sealed class LemonSqueezyLicenseClient(HttpClient http) : IDisposable
{
    private static readonly Uri Endpoint = new("https://api.lemonsqueezy.com/v1/licenses/");
    public async Task<LemonLicenseResponse> SendAsync(string operation, string key, string? instance, CancellationToken token)
    {
        if (operation is not ("activate" or "validate" or "deactivate")) throw new ArgumentException("Unknown license operation.");
        var fields = new Dictionary<string, string> { ["license_key"] = key };
        if (instance is not null) fields[operation == "activate" ? "instance_name" : "instance_id"] = instance;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, operation));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            throw new HttpRequestException("License service temporarily unavailable.");
        if (!response.IsSuccessStatusCode && response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity))
            throw new LicenseOperationException("The license service rejected the request. Contact hello@mablook.com.");
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(bytes, token)) > 0)
        {
            if (buffer.Length + count > 65536) throw new LicenseOperationException("Unexpected license response. Please try again.");
            buffer.Write(bytes, 0, count);
        }
        try
        {
            var result = JsonSerializer.Deserialize<LemonLicenseResponse>(buffer.ToArray())
                ?? throw new JsonException();
            // An HTTP error can never assert a successful activation/validation.
            if (!response.IsSuccessStatusCode) return new LemonLicenseResponse();
            return result;
        }
        catch (JsonException) { throw new LicenseOperationException("Unexpected license response. Please try again."); }
    }
    public void Dispose() => http.Dispose();
}

internal sealed class LicenseOperationException(string message) : Exception(message);
internal sealed class LemonLicenseResponse
{
    [JsonPropertyName("valid")] public bool Valid { get; init; }
    [JsonPropertyName("activated")] public bool Activated { get; init; }
    [JsonPropertyName("deactivated")] public bool Deactivated { get; init; }
    [JsonPropertyName("license_key")] public LemonKey? Key { get; init; }
    [JsonPropertyName("instance")] public LemonInstance? Instance { get; init; }
    [JsonPropertyName("meta")] public LemonMeta? Meta { get; init; }
}
internal sealed class LemonKey
{
    [JsonPropertyName("key")] public string? Key { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; init; }
}
internal sealed class LemonInstance
{
    [JsonPropertyName("id")] public string? Id { get; init; }
}
internal sealed class LemonMeta
{
    [JsonPropertyName("store_id")] public int StoreId { get; init; }
    [JsonPropertyName("product_id")] public int ProductId { get; init; }
    [JsonPropertyName("variant_id")] public int VariantId { get; init; }
}
