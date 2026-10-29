namespace Cartenvo.MerchantCenter.Core;

/// <summary>
/// Cleans an item to Google's limits and lists what's wrong with it. Items with an error are held back, because
/// Google would reject them; everything else is sent.
/// </summary>
public static class FeedChecks
{
    public const int MaxId = 50;
    public const int MaxTitle = 150;
    public const int MaxDescription = 5000;
    public const int MaxBrand = 70;
    public const int MaxMpn = 70;
    public const int MaxProductType = 750;
    public const int MaxAdditionalImages = 10;
    public const int MaxCustomLabel = 100;

    /// <param name="item">Item to clean and check. It's changed in place.</param>
    /// <param name="noIdentifiersMeansCustom">
    /// Sends identifier_exists=no for items without GTIN and MPN (for custom-made or handmade products).
    /// </param>
    public static List<FeedIssue> Apply(FeedItem item, bool noIdentifiersMeansCustom = false)
    {
        var issues = new List<FeedIssue>();

        item.Id = Text.Clean(item.Id);
        if (item.Id.Length > MaxId)
            item.Id = item.Id[..MaxId];
        if (item.Id.Length == 0)
            issues.Add(IssueCodes.Create(IssueCodes.MissingId, "id"));

        item.Title = Text.Truncate(Text.Clean(item.Title), MaxTitle, out var titleCut);
        if (item.Title.Length == 0)
            issues.Add(IssueCodes.Create(IssueCodes.MissingTitle, "title"));
        else
        {
            if (titleCut)
                issues.Add(IssueCodes.Create(IssueCodes.TitleTruncated, "title"));
            if (Text.IsAllCaps(item.Title))
                issues.Add(IssueCodes.Create(IssueCodes.TitleAllCaps, "title"));
        }

        item.Description = Text.Truncate(Text.Clean(item.Description), MaxDescription, out var descriptionCut);
        if (item.Description.Length == 0)
            issues.Add(IssueCodes.Create(IssueCodes.MissingDescription, "description"));
        else if (descriptionCut)
            issues.Add(IssueCodes.Create(IssueCodes.DescriptionTruncated, "description"));

        for (var i = 0; i < FeedItem.CustomLabelCount; i++)
        {
            var label = Text.Truncate(Text.Clean(item.CustomLabels[i] ?? ""), MaxCustomLabel, out _);
            item.CustomLabels[i] = label.Length == 0 ? null : label;
        }

        item.Link = item.Link?.Trim() ?? "";
        if (item.Link.Length == 0)
            issues.Add(IssueCodes.Create(IssueCodes.MissingLink, "link"));
        else if (!item.Link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            issues.Add(IssueCodes.Create(IssueCodes.NotHttps, "link"));

        if (string.IsNullOrWhiteSpace(item.ImageLink))
        {
            item.ImageLink = null;
            issues.Add(IssueCodes.Create(IssueCodes.MissingImage, "image_link"));
        }
        if (item.AdditionalImageLinks.Count > MaxAdditionalImages)
            item.AdditionalImageLinks.RemoveRange(MaxAdditionalImages, item.AdditionalImageLinks.Count - MaxAdditionalImages);

        if (item.Price <= 0)
            issues.Add(IssueCodes.Create(IssueCodes.ZeroPrice, "price"));
        if (item.SalePrice is { } sale && (sale <= 0 || sale >= item.Price))
        {
            item.SalePrice = null;
            item.SaleStartUtc = null;
            item.SaleEndUtc = null;
        }

        item.Brand = Limit(item.Brand, MaxBrand);
        item.Mpn = Limit(item.Mpn, MaxMpn);
        item.ProductType = Limit(item.ProductType, MaxProductType);
        item.Gtin = Gtin.Normalize(item.Gtin);
        if (item.Gtin is not null && !Gtin.IsValid(item.Gtin))
        {
            // Google disapproves products with an invalid GTIN. Leaving it out keeps the product listed.
            item.Gtin = null;
            issues.Add(IssueCodes.Create(IssueCodes.InvalidGtin, "gtin"));
        }

        if (item.Brand is null && item.Condition == "new")
            issues.Add(IssueCodes.Create(IssueCodes.MissingBrand, "brand"));

        var hasIdentifiers = item.Gtin is not null || (item.Mpn is not null && item.Brand is not null);
        if (!hasIdentifiers)
        {
            if (noIdentifiersMeansCustom && item.Gtin is null && item.Mpn is null)
                item.IdentifierExists = false;
            else
                issues.Add(IssueCodes.Create(IssueCodes.MissingIdentifiers, "gtin"));
        }

        if (item.GoogleProductCategory is null)
            issues.Add(IssueCodes.Create(IssueCodes.UnmappedCategory, "google_product_category"));
        if (item.Availability == ItemAvailability.OutOfStock)
            issues.Add(IssueCodes.Create(IssueCodes.OutOfStock, "availability"));

        return issues;
    }

    public static bool IsHeldBack(IEnumerable<FeedIssue> issues) => issues.Any(issue => issue.Severity == IssueSeverity.Error);

    private static string? Limit(string? value, int max)
    {
        var cleaned = Text.Clean(value);
        if (cleaned.Length == 0)
            return null;
        return cleaned.Length > max ? cleaned[..max].TrimEnd() : cleaned;
    }
}
