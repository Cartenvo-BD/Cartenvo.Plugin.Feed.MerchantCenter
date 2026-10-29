using Nop.Core.Configuration;

namespace Cartenvo.Plugin.Feed.MerchantCenter;

public class MerchantCenterSettings : ISettings
{
    /// <summary><see cref="Cartenvo.MerchantCenter.Core.OfferIdSource"/> as an int.</summary>
    public int OfferIdSource { get; set; }

    /// <summary>Brand for products without a manufacturer. Empty sends no brand.</summary>
    public string DefaultBrand { get; set; } = "";

    /// <summary>Send each attribute combination as its own item, grouped under the product.</summary>
    public bool SplitCombinations { get; set; } = true;

    /// <summary>Send identifier_exists=no for products without GTIN and part number.</summary>
    public bool NoIdentifiersMeansCustom { get; set; }

    public int PictureSize { get; set; } = 1000;
    public int MaxAdditionalImages { get; set; } = 10;

    /// <summary>How often the scheduled task rebuilds each feed.</summary>
    public int RegenerateEveryHours { get; set; } = 6;

    /// <summary>Product attribute names that hold the colour, comma-separated.</summary>
    public string ColorAttributeNames { get; set; } = "Color,Colour";

    /// <summary>Product attribute names that hold the size, comma-separated.</summary>
    public string SizeAttributeNames { get; set; } = "Size";

    /// <summary>Empty, or Google's gender value (male, female, unisex) for clothing stores.</summary>
    public string DefaultGender { get; set; } = "";

    /// <summary>Empty, or Google's age_group value (adult, kids, ...) for clothing stores.</summary>
    public string DefaultAgeGroup { get; set; } = "";

    public bool IncludeShippingWeight { get; set; } = true;

    /// <summary>Send the product's short description when the full one is empty.</summary>
    public bool UseShortDescriptionFallback { get; set; } = true;

    // Automatic sync through the Merchant API (release 2)

    /// <summary>Merchant Center account ID (the number at the top of Merchant Center).</summary>
    public string ApiMerchantId { get; set; } = "";

    /// <summary>The service account JSON key, encrypted with the store's encryption key.</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    /// <summary>Service account email and Google Cloud project of the key, shown on the connection page.</summary>
    public string ApiClientEmail { get; set; } = "";
    public string ApiProjectId { get; set; } = "";

    /// <summary>Merchant Center account name, read when the connection was tested.</summary>
    public string ApiAccountName { get; set; } = "";

    /// <summary>The key's Google Cloud project is registered with the account (registerGcp).</summary>
    public bool ApiRegistered { get; set; }

    /// <summary>The plugin's own API data source: accounts/{id}/dataSources/{id}. Empty until connected.</summary>
    public string ApiDataSource { get; set; } = "";

    /// <summary>Calls to Google at the same time. Google suggests parallel calls over batches.</summary>
    public int ApiParallelRequests { get; set; } = 5;

    /// <summary>Another Merchant API address, for testing against a local stand-in. Empty uses Google's.</summary>
    public string ApiBaseUrl { get; set; } = "";

    // Status read back from Google (release 3)

    /// <summary>When product statuses were last read from Google. Null: never.</summary>
    public DateTime? StatusReadOnUtc { get; set; }

    /// <summary>Why the last status read failed, in the admin's language. Empty when it worked.</summary>
    public string StatusReadError { get; set; } = "";

    /// <summary>How often statuses are read. Google updates them a few times a day.</summary>
    public int StatusEveryHours { get; set; } = 12;

    /// <summary>Statuses can be read with a key that has access and is registered; a data source isn't needed.</summary>
    public bool ApiCanRead => !string.IsNullOrEmpty(ApiKeyEncrypted) && !string.IsNullOrEmpty(ApiMerchantId) && ApiRegistered;

    public bool ApiConnected => !string.IsNullOrEmpty(ApiKeyEncrypted) && !string.IsNullOrEmpty(ApiMerchantId) && !string.IsNullOrEmpty(ApiDataSource);
}
