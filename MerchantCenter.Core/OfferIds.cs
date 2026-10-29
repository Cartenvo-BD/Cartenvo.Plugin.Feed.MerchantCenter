namespace Cartenvo.MerchantCenter.Core;

/// <summary>Which value becomes the Google item ID. Stored as an int in settings, so never renumber.</summary>
public enum OfferIdSource
{
    /// <summary>SKU, or the product ID when there's no SKU.</summary>
    SkuThenProductId = 0,
    ProductId = 1,
    Sku = 2
}

public static class OfferIds
{
    /// <summary>The item ID for a product. Null when the source is SKU and the product has none.</summary>
    public static string? ForProduct(OfferIdSource source, int productId, string? sku)
    {
        sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
        return source switch
        {
            OfferIdSource.ProductId => productId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OfferIdSource.Sku => sku,
            _ => sku ?? productId.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// The item ID for an attribute combination: its own SKU when it has one (and SKUs are used), otherwise the product's
    /// ID with the combination ID after it.
    /// </summary>
    public static string? ForCombination(OfferIdSource source, string? productOfferId, int combinationId, string? combinationSku)
    {
        if (source != OfferIdSource.ProductId && !string.IsNullOrWhiteSpace(combinationSku))
            return combinationSku.Trim();
        return productOfferId is null ? null : $"{productOfferId}-{combinationId}";
    }

    /// <summary>
    /// The Merchant API product name part for an item: <c>{language}~{feedLabel}~{offerId}</c>. When the offer ID
    /// contains <c>/</c>, <c>%</c> or <c>~</c>, the whole name is sent as unpadded base64url. Used by the API sync.
    /// </summary>
    public static string MerchantApiName(string contentLanguage, string feedLabel, string offerId)
    {
        var name = $"{contentLanguage}~{feedLabel}~{offerId}";
        if (offerId.IndexOfAny(['/', '%', '~']) < 0)
            return name;
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
