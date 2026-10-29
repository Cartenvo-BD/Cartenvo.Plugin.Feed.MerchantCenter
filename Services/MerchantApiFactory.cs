using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Nop.Services.Configuration;
using Nop.Services.Security;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Creates Merchant API clients from the saved connection. The key is stored encrypted and only decrypted here.</summary>
public class MerchantApiFactory(ISettingService settingService, IEncryptionService encryption, IHttpClientFactory httpClients)
{
    // Access tokens last an hour; one token source per key keeps them across requests and scheduled task runs.
    private static readonly ConcurrentDictionary<string, GoogleTokenSource> TokenSources = new();

    public Task<MerchantCenterSettings> LoadSettingsAsync() => settingService.LoadSettingAsync<MerchantCenterSettings>();

    public string Encrypt(string keyJson) => encryption.EncryptText(keyJson);

    public ServiceAccountKey ReadKey(MerchantCenterSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKeyEncrypted))
            return null;
        try
        {
            return ServiceAccountKey.TryParse(encryption.DecryptText(settings.ApiKeyEncrypted), out _);
        }
        catch (CryptographicException)
        {
            return null; // The store's encryption key changed; the key has to be pasted again.
        }
    }

    /// <summary>A client for the saved account, or null when no key or account ID is saved.</summary>
    public MerchantApiClient Create(MerchantCenterSettings settings)
    {
        var key = ReadKey(settings);
        return key == null || string.IsNullOrWhiteSpace(settings.ApiMerchantId) ? null : Create(key, settings.ApiMerchantId, settings.ApiBaseUrl);
    }

    /// <summary>A client for a key and account that may not be saved yet (testing a new connection).</summary>
    public MerchantApiClient Create(ServiceAccountKey key, string merchantId, string baseUrl)
    {
        var http = httpClients.CreateClient(MerchantCenterDefaults.HttpClientName);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ClientEmail + "\n" + key.PrivateKey + "\n" + key.TokenUri)));
        var tokens = TokenSources.GetOrAdd(id, _ => new GoogleTokenSource(key, httpClients.CreateClient(MerchantCenterDefaults.HttpClientName)));
        return new MerchantApiClient(http, tokens.GetTokenAsync, merchantId.Trim(), baseUrl);
    }

    /// <summary>Forgets cached tokens, after the key is replaced or removed.</summary>
    public static void ClearTokens() => TokenSources.Clear();
}
