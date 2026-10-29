namespace Cartenvo.MerchantCenter.Core;

public enum IssueSeverity
{
    /// <summary>Sent as it is; worth knowing.</summary>
    Info = 0,

    /// <summary>Sent, but Google may limit it or the plugin changed something.</summary>
    Warning = 1,

    /// <summary>Held back: Google would reject it.</summary>
    Error = 2
}

public sealed record FeedIssue(string Code, IssueSeverity Severity, string? Attribute = null);

/// <summary>Issue codes. They are stored in the database and used as resource keys, so never rename one.</summary>
public static class IssueCodes
{
    // Held back
    public const string MissingId = "missing_id";
    public const string DuplicateId = "duplicate_id";
    public const string MissingTitle = "missing_title";
    public const string MissingDescription = "missing_description";
    public const string MissingLink = "missing_link";
    public const string MissingImage = "missing_image";
    public const string ZeroPrice = "zero_price";

    // Sent, with a warning
    public const string InvalidGtin = "invalid_gtin";
    public const string MissingBrand = "missing_brand";
    public const string MissingIdentifiers = "missing_identifiers";
    public const string TitleTruncated = "title_truncated";
    public const string DescriptionTruncated = "description_truncated";
    public const string TitleAllCaps = "title_all_caps";
    public const string NotHttps = "not_https";
    public const string UnmappedCategory = "unmapped_category";

    // Sent, for information
    public const string OutOfStock = "out_of_stock";

    public static readonly IReadOnlyDictionary<string, IssueSeverity> Severity = new Dictionary<string, IssueSeverity>
    {
        [MissingId] = IssueSeverity.Error,
        [DuplicateId] = IssueSeverity.Error,
        [MissingTitle] = IssueSeverity.Error,
        [MissingDescription] = IssueSeverity.Error,
        [MissingLink] = IssueSeverity.Error,
        [MissingImage] = IssueSeverity.Error,
        [ZeroPrice] = IssueSeverity.Error,
        [InvalidGtin] = IssueSeverity.Warning,
        [MissingBrand] = IssueSeverity.Warning,
        [MissingIdentifiers] = IssueSeverity.Warning,
        [TitleTruncated] = IssueSeverity.Warning,
        [DescriptionTruncated] = IssueSeverity.Warning,
        [TitleAllCaps] = IssueSeverity.Warning,
        [NotHttps] = IssueSeverity.Warning,
        [UnmappedCategory] = IssueSeverity.Info,
        [OutOfStock] = IssueSeverity.Info
    };

    /// <summary>All codes, most serious first, in the order the admin lists them.</summary>
    public static IEnumerable<string> All => Severity.OrderByDescending(pair => pair.Value).Select(pair => pair.Key);

    public static FeedIssue Create(string code, string? attribute = null) => new(code, Severity[code], attribute);
}
