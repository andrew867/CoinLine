using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HostPlatform.Api.Options;

namespace HostPlatform.Api.Hosting;

internal static class ShadyBankAuthorizationClient
{
    internal const string ClientName = "shadybank-authorization";
    private const int MaximumResponseBytes = 2048;

    internal static Uri Validate(ShadyBankAuthorizationOptions options)
    {
        if (!Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || options.AllowLoopbackHttp && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            uri.AbsolutePath != "/api/merchant/authorize" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !IsOpaqueToken(options.ServiceKey, 32, 128) || options.Bindings is not { Length: > 0 } ||
            options.Bindings.Any(binding => binding == null || binding.TerminalId == Guid.Empty ||
                !SyntheticLabCardAuthorizationProvider.IsReservedDestination(binding.Destination) || !IsOpaqueToken(binding.InstrumentToken, 43, 43)) ||
            options.Bindings.GroupBy(binding => (binding.TerminalId, binding.Destination)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("ShadyBank requires a trusted merchant endpoint, private key and distinct synthetic lab instrument bindings.");
        if (options.CaptureUploadedCalls && (!options.Bindings.Any(binding => binding.AllowCaptureUploadedCalls) ||
            options.Bindings.Any(binding => binding.AllowCaptureUploadedCalls && binding.TerminalClockOffsetMinutes is not (>= -840 and <= 840))))
            throw new InvalidOperationException("ShadyBank capture requires an explicitly qualified binding and its phone-local clock offset.");
        return uri;
    }

    internal static Uri CaptureEndpoint(ShadyBankAuthorizationOptions options)
    {
        if (!options.Enabled || !options.CaptureUploadedCalls)
            throw new InvalidOperationException("ShadyBank capture is disabled.");
        return new Uri(Validate(options), "/api/merchant/capture");
    }

    private static bool IsOpaqueToken(string? token, int min, int max) => token != null && token.Length >= min && token.Length <= max
        && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // Generic instrument API client. Neither the protected terminal request nor its card fields are accepted here.
    internal static async Task<string?> AuthorizeAsync(HttpClient client, Uri endpoint, string serviceKey,
        string instrumentToken, decimal amount, Guid requestId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Shadybank-Merchant-Key", serviceKey);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new
        {
            instrument_token = instrumentToken,
            amount = amount.ToString("F2", CultureInfo.InvariantCulture),
            request_id = requestId.ToString("D")
        }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("Merchant authorization HTTP failure.", null, response.StatusCode);
        using var document = await ReadSmallJsonAsync(response, ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            throw new InvalidCardProviderResponseException();
        var fields = root.EnumerateObject().ToArray();
        if (status.GetString() == "declined" && fields.Length == 1) return null;
        if (status.GetString() == "approved" && fields.Length == 2 && root.TryGetProperty("authorization_code", out var reference)
            && reference.ValueKind == JsonValueKind.String && reference.GetString() is { Length: 6 } code && code.All(char.IsAsciiDigit))
            return code;
        throw new InvalidCardProviderResponseException();
    }

    // No instrument token or terminal card fields are needed to post a previously approved reservation.
    internal static async Task CaptureAsync(HttpClient client, Uri endpoint, string serviceKey,
        Guid requestId, Guid settlementId, decimal amount, CancellationToken ct)
    {
        if (endpoint.AbsolutePath != "/api/merchant/capture" || requestId == Guid.Empty || settlementId == Guid.Empty ||
            requestId == settlementId || amount is <= 0 or > 655.35m || decimal.Round(amount, 2) != amount)
            throw new InvalidOperationException("Invalid scoped merchant capture request.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-Shadybank-Merchant-Key", serviceKey);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request_id = requestId.ToString("D"),
            settlement_id = settlementId.ToString("D"),
            amount = amount.ToString("F2", CultureInfo.InvariantCulture)
        }));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("Merchant capture HTTP failure.", null, response.StatusCode);
        using var document = await ReadSmallJsonAsync(response, ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
            !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "posted" ||
            !root.TryGetProperty("settlement_id", out var receipt) || receipt.ValueKind != JsonValueKind.String ||
            receipt.GetString() != settlementId.ToString("D"))
            throw new InvalidCardProviderResponseException();
    }

    private static async Task<JsonDocument> ReadSmallJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
            response.Content.Headers.ContentLength is > MaximumResponseBytes)
            throw new InvalidCardProviderResponseException();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaximumResponseBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (count == 0) break;
            length += count;
        }
        if (length > MaximumResponseBytes) throw new InvalidCardProviderResponseException();
        try
        {
            return JsonDocument.Parse(buffer.AsMemory(0, length));
        }
        catch (JsonException) { throw new InvalidCardProviderResponseException(); }
    }
}
