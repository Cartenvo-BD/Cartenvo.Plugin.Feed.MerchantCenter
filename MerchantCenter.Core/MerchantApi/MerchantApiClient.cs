using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cartenvo.MerchantCenter.Core.MerchantApi;

/// <summary>The outcome of one Merchant API call.</summary>
public sealed record ApiResult(int Status, JsonNode? Json, string? ErrorStatus, string? ErrorReason, string? Message)
{
    public bool Ok => Status is >= 200 and < 300;

    /// <summary>What went wrong, in words the admin page can explain. Null when the call worked.</summary>
    public string? Problem => Ok ? null : ApiProblems.Classify(this);

    public static ApiResult Failed(string reason, string message) => new(0, null, null, reason, message);
}

public static class ApiProblems
{
    public const string Network = "network";
    public const string BadKey = "bad_key";
    public const string NotRegistered = "not_registered";
    public const string ApiDisabled = "api_disabled";
    public const string NoAccess = "no_access";
    public const string NotFound = "not_found";
    public const string Quota = "quota";
    public const string Rejected = "rejected";
    public const string Other = "other";

    public static string Classify(ApiResult result)
    {
        var message = result.Message ?? "";
        if (result.Status == 0)
            return result.ErrorReason == BadKey ? BadKey : Network;
        if (result.ErrorReason == "SERVICE_DISABLED"
            || message.Contains("has not been used in project", StringComparison.OrdinalIgnoreCase)
            || message.Contains("it is disabled", StringComparison.OrdinalIgnoreCase))
            return ApiDisabled;
        if (result.ErrorReason is "AUTH_GCP_NOT_REGISTERED" or "GCP_NOT_REGISTERED"
            || message.Contains("not registered", StringComparison.OrdinalIgnoreCase))
            return NotRegistered;
        return result.Status switch
        {
            401 or 403 => NoAccess,
            404 => NotFound,
            429 => Quota,
            400 => Rejected,
            _ => Other
        };
    }
}

/// <summary>
/// Merchant API v1 over HTTPS and JSON. Retries on rate limits and server errors with exponential back-off, as Google
/// recommends. One client per Merchant Center account.
/// </summary>
public sealed class MerchantApiClient(HttpClient http, Func<CancellationToken, Task<string>> accessToken, string accountId,
    string? baseUrl = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public const string DefaultBaseUrl = "https://merchantapi.googleapis.com";
    public const int MaxAttempts = 5;

    private readonly string _base = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl).TrimEnd('/');
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public string AccountId => accountId;
    public string Account => $"accounts/{accountId}";

    /// <summary>The Merchant Center account: checks the key, the API and access in one call.</summary>
    public Task<ApiResult> GetAccountAsync(CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, $"/accounts/v1/{Account}", null, cancellationToken);

    /// <summary>Links the key's Google Cloud project to the account. Needed once before any other call works.</summary>
    public Task<ApiResult> RegisterGcpAsync(string? developerEmail, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject();
        if (!string.IsNullOrWhiteSpace(developerEmail))
            body["developerEmail"] = developerEmail.Trim();
        return SendAsync(HttpMethod.Post, $"/accounts/v1/{Account}/developerRegistration:registerGcp", body, cancellationToken);
    }

    /// <summary>An API primary product source with no fixed feed label or language, so one source serves every feed.</summary>
    public Task<ApiResult> CreateDataSourceAsync(string displayName, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/datasources/v1/{Account}/dataSources", new JsonObject
        {
            ["displayName"] = displayName,
            ["primaryProductDataSource"] = new JsonObject()
        }, cancellationToken);

    public Task<ApiResult> GetDataSourceAsync(string dataSourceName, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, $"/datasources/v1/{dataSourceName}", null, cancellationToken);

    public Task<ApiResult> InsertProductAsync(string dataSourceName, JsonObject productInput, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"/products/v1/{Account}/productInputs:insert?dataSource={Uri.EscapeDataString(dataSourceName)}",
            productInput, cancellationToken);

    /// <summary>Removes a product from the plugin's data source. A product that's already gone counts as removed.</summary>
    public async Task<ApiResult> DeleteProductAsync(string dataSourceName, string contentLanguage, string feedLabel, string offerId,
        CancellationToken cancellationToken = default)
    {
        var name = ProductInputs.EncodedName(contentLanguage, feedLabel, offerId);
        var result = await SendAsync(HttpMethod.Delete,
            $"/products/v1/{Account}/productInputs/{name}?dataSource={Uri.EscapeDataString(dataSourceName)}", null, cancellationToken);
        return result.Status == 404 ? result with { Status = 204 } : result;
    }

    private async Task<ApiResult> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        var json = body?.ToJsonString();
        for (var attempt = 1; ; attempt++)
        {
            string token;
            try
            {
                token = await accessToken(cancellationToken);
            }
            catch (GoogleAuthException ex)
            {
                return ApiResult.Failed(ex.Code, ex.Message);
            }

            using var request = new HttpRequestMessage(method, _base + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await _delay(Backoff(attempt, null), cancellationToken);
                    continue;
                }
                return ApiResult.Failed(ApiProblems.Network, ex.Message);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (Retry(response.StatusCode) && attempt < MaxAttempts)
                {
                    await _delay(Backoff(attempt, response.Headers.RetryAfter?.Delta), cancellationToken);
                    continue;
                }
                return Parse(status, text);
            }
        }
    }

    private static bool Retry(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    /// <summary>1, 2, 4, 8 seconds plus up to a second at random, or what Google asks for (at most a minute).</summary>
    public static TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } asked && asked > TimeSpan.Zero)
            return asked > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : asked;
        return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
    }

    /// <summary>Google errors look like {"error":{"code":403,"message":"...","status":"PERMISSION_DENIED","details":[{"reason":"..."}]}}.</summary>
    public static ApiResult Parse(int status, string text)
    {
        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
        if (status is >= 200 and < 300)
            return new ApiResult(status, json, null, null, null);

        var error = json?["error"] as JsonObject;
        string? reason = null;
        if (error?["details"] is JsonArray details)
            foreach (var detail in details)
                if (detail?["reason"] is JsonValue value && value.TryGetValue<string>(out var found))
                {
                    reason = found;
                    break;
                }
        string? Text(string name) => error?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var message = Text("message") ?? (string.IsNullOrWhiteSpace(text) ? $"HTTP {status}" : text.Length > 500 ? text[..500] : text);
        return new ApiResult(status, json, Text("status"), reason, message);
    }
}
