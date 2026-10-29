using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cartenvo.MerchantCenter.Core.MerchantApi;

/// <summary>A Google sign-in that failed: a bad or revoked key, or no connection to Google.</summary>
public sealed class GoogleAuthException(string code, string message) : Exception(message)
{
    /// <summary>bad_key (Google refused the key) or network.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// Access tokens for a service account, from Google's OAuth token endpoint with a signed JWT (RFC 7523). Done here, not
/// with Google.Apis.Auth, so the plugin doesn't depend on the version each nopCommerce release ships. Tokens are reused
/// until a minute before they expire.
/// </summary>
public sealed class GoogleTokenSource(ServiceAccountKey key, HttpClient http, Func<DateTimeOffset>? clock = null)
{
    public const string Scope = "https://www.googleapis.com/auth/content";

    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public ServiceAccountKey Key => key;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_token != null && _now() < _expires)
            return _token;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token != null && _now() < _expires)
                return _token;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = CreateAssertion(key, _now())
            });
            HttpResponseMessage response;
            try
            {
                response = await http.PostAsync(key.TokenUri, form, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new GoogleAuthException("network", ex.Message);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GoogleAuthException("network", ex.Message);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? json = null;
            try { json = JsonNode.Parse(body); } catch (JsonException) { }
            if (!response.IsSuccessStatusCode || json?["access_token"] is null)
            {
                // {"error":"invalid_grant","error_description":"Invalid JWT Signature."}
                var description = json?["error_description"]?.GetValue<string>() ?? json?["error"]?.GetValue<string>() ?? $"HTTP {(int)response.StatusCode}";
                throw new GoogleAuthException("bad_key", description);
            }
            _token = json["access_token"]!.GetValue<string>();
            var seconds = json["expires_in"]?.GetValue<int>() ?? 3600;
            _expires = _now().AddSeconds(Math.Max(60, seconds - 60));
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The signed JWT Google exchanges for an access token, valid for one hour.</summary>
    public static string CreateAssertion(ServiceAccountKey key, DateTimeOffset now)
    {
        var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT" };
        if (!string.IsNullOrEmpty(key.PrivateKeyId))
            header["kid"] = key.PrivateKeyId;
        var issued = now.ToUnixTimeSeconds();
        var claims = new JsonObject
        {
            ["iss"] = key.ClientEmail,
            ["scope"] = Scope,
            ["aud"] = key.TokenUri,
            ["iat"] = issued,
            ["exp"] = issued + 3600
        };
        var unsigned = $"{Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()))}";
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(key.PrivateKey);
        }
        catch (ArgumentException ex)
        {
            throw new GoogleAuthException("bad_key", "The private key in the JSON file can't be read: " + ex.Message);
        }
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return unsigned + "." + Base64Url(signature);
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
