using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Cartenvo.MerchantCenter.Core.MerchantApi;

/// <summary>
/// Turns a feed item into a Merchant API v1 ProductInput. Only the fields below are ever sent: an unknown field makes
/// Google reject the whole product. Field names: developers.google.com/merchant/api/reference/rest/products_v1/ProductAttributes.
/// </summary>
public static class ProductInputs
{
    public static JsonObject Build(FeedItem item, string contentLanguage, string feedLabel)
    {
        var attributes = new JsonObject();
        void Set(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
                attributes[name] = value;
        }

        Set("title", item.Title);
        Set("description", item.Description);
        Set("link", item.Link);
        Set("imageLink", item.ImageLink);
        if (item.AdditionalImageLinks.Count > 0)
            attributes["additionalImageLinks"] = Array(item.AdditionalImageLinks);
        attributes["availability"] = Availability(item.Availability);
        if (item.AvailabilityDateUtc is { } availableOn && item.Availability is ItemAvailability.Preorder or ItemAvailability.Backorder)
            attributes["availabilityDate"] = Timestamp(availableOn);
        attributes["price"] = Price(item.Price, item.Currency);
        if (item.SalePrice is { } sale)
        {
            attributes["salePrice"] = Price(sale, item.Currency);
            if (item.SaleStartUtc is not null || item.SaleEndUtc is not null)
            {
                var interval = new JsonObject();
                if (item.SaleStartUtc is { } start)
                    interval["startTime"] = Timestamp(start);
                if (item.SaleEndUtc is { } end)
                    interval["endTime"] = Timestamp(end);
                attributes["salePriceEffectiveDate"] = interval;
            }
        }
        Set("brand", item.Brand);
        if (!string.IsNullOrEmpty(item.Gtin))
            attributes["gtins"] = Array([item.Gtin]);
        Set("mpn", item.Mpn);
        if (item.IdentifierExists == false)
            attributes["identifierExists"] = false;
        attributes["condition"] = item.Condition?.ToLowerInvariant() switch
        {
            "used" => "USED",
            "refurbished" => "REFURBISHED",
            _ => "NEW"
        };
        if (item.GoogleProductCategory is { } category)
            attributes["googleProductCategory"] = category.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(item.ProductType))
            attributes["productTypes"] = Array([item.ProductType]);
        Set("itemGroupId", item.ItemGroupId);
        Set("color", item.Color);
        Set("size", item.Size);
        if (Gender(item.Gender) is { } gender)
            attributes["gender"] = gender;
        if (AgeGroup(item.AgeGroup) is { } ageGroup)
            attributes["ageGroup"] = ageGroup;
        if (item.ShippingWeight is > 0 && item.ShippingWeightUnit is not null)
            attributes["shippingWeight"] = new JsonObject
            {
                ["value"] = (double)Math.Round(item.ShippingWeight.Value, 3),
                ["unit"] = item.ShippingWeightUnit
            };
        for (var i = 0; i < FeedItem.CustomLabelCount; i++)
            Set("customLabel" + i.ToString(CultureInfo.InvariantCulture), item.CustomLabels[i]);

        return new JsonObject
        {
            ["offerId"] = item.Id,
            ["contentLanguage"] = contentLanguage,
            ["feedLabel"] = feedLabel,
            ["productAttributes"] = attributes
        };
    }

    /// <summary>SHA-256 of the input, to skip products that haven't changed since they were last sent.</summary>
    public static string Hash(JsonObject input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToJsonString()))).ToLowerInvariant();

    /// <summary>Google's price: amount in millionths, as a string, and the currency code.</summary>
    public static JsonObject Price(decimal amount, string currency) => new()
    {
        ["amountMicros"] = ((long)Math.Round(Math.Round(amount, 2, MidpointRounding.AwayFromZero) * 1_000_000m)).ToString(CultureInfo.InvariantCulture),
        ["currencyCode"] = currency.ToUpperInvariant()
    };

    public static string Timestamp(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string Availability(ItemAvailability availability) => availability switch
    {
        ItemAvailability.OutOfStock => "OUT_OF_STOCK",
        ItemAvailability.Preorder => "PREORDER",
        ItemAvailability.Backorder => "BACKORDER",
        _ => "IN_STOCK"
    };

    private static string? Gender(string? value) => value?.ToLowerInvariant() switch
    {
        "male" => "MALE",
        "female" => "FEMALE",
        "unisex" => "UNISEX",
        _ => null
    };

    private static string? AgeGroup(string? value) => value?.ToLowerInvariant() switch
    {
        "newborn" => "NEWBORN",
        "infant" => "INFANT",
        "toddler" => "TODDLER",
        "kids" => "KIDS",
        "adult" => "ADULT",
        _ => null
    };

    private static JsonArray Array(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }

    /// <summary>
    /// The product input's name segment for a delete: <c>{language}~{feedLabel}~{offerId}</c> as unpadded base64url,
    /// the format Google recommends because it works for every offer ID.
    /// </summary>
    public static string EncodedName(string contentLanguage, string feedLabel, string offerId)
        => GoogleTokenSource.Base64Url(Encoding.UTF8.GetBytes($"{contentLanguage}~{feedLabel}~{offerId}"));
}
