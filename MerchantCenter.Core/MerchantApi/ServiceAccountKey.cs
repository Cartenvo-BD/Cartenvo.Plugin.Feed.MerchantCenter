using System.Text.Json;

namespace Cartenvo.MerchantCenter.Core.MerchantApi;

/// <summary>The parts of a Google Cloud service account JSON key the plugin uses.</summary>
public sealed record ServiceAccountKey(string ClientEmail, string PrivateKey, string PrivateKeyId, string ProjectId, string TokenUri)
{
    public const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// Reads a service account key file. On failure, <paramref name="error"/> is one of: empty, not_json,
    /// not_service_account (an OAuth client or API key file), missing_fields.
    /// </summary>
    public static ServiceAccountKey? TryParse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json.Trim());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "not_json";
                return null;
            }
            if (Get(root, "type") != "service_account")
            {
                error = "not_service_account";
                return null;
            }
            var email = Get(root, "client_email");
            var key = Get(root, "private_key");
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(key) || !key.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                error = "missing_fields";
                return null;
            }
            var tokenUri = Get(root, "token_uri");
            return new ServiceAccountKey(email, key, Get(root, "private_key_id") ?? "", Get(root, "project_id") ?? "",
                string.IsNullOrEmpty(tokenUri) ? DefaultTokenUri : tokenUri);
        }
        catch (JsonException)
        {
            error = "not_json";
            return null;
        }
    }

    private static string? Get(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
