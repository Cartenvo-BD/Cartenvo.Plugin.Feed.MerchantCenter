using Nop.Core;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Domain;

/// <summary>One feed: a store, country, language and currency, with its own URL.</summary>
public class MerchantFeed : BaseEntity
{
    public string Name { get; set; }
    public int StoreId { get; set; }

    /// <summary>Two-letter country code the feed is for. Also the Merchant Center feed label.</summary>
    public string CountryCode { get; set; }
    public int LanguageId { get; set; }
    public int CurrencyId { get; set; }

    /// <summary>Prices include tax. Google expects this everywhere except the US and Canada.</summary>
    public bool PricesIncludeTax { get; set; }
    public bool Enabled { get; set; }

    /// <summary>Secret part of the feed URL, so only Merchant Center (and whoever has the link) can read the feed.</summary>
    public string Token { get; set; }

    /// <summary>Comma-separated category IDs. Empty includes all categories.</summary>
    public string CategoryIds { get; set; }
    public bool OnlyInStock { get; set; }
    public decimal? MinPrice { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public DateTime? LastGeneratedOnUtc { get; set; }
    public int LastItemCount { get; set; }
    public int LastHeldBackCount { get; set; }
    public int LastWarningCount { get; set; }
    public int LastDurationMs { get; set; }
    public string LastError { get; set; }

    /// <summary>Issue counts from the last generation, as JSON: {"missing_image": 3, ...}.</summary>
    public string IssueSummaryJson { get; set; }

    /// <summary>The owner said they added the feed URL in Merchant Center. Only used for the setup checklist.</summary>
    public bool AddedToMerchantCenter { get; set; }

    /// <summary><see cref="FeedSyncMode"/> as an int: a feed file, or automatic sync through the Merchant API.</summary>
    public int SyncMode { get; set; }

    /// <summary>When every product was last compared with what Google has (the daily full sync).</summary>
    public DateTime? LastFullSyncOnUtc { get; set; }

    /// <summary>When changed products were last sent.</summary>
    public DateTime? LastSyncOnUtc { get; set; }

    /// <summary>Why the last sync stopped, when it couldn't reach Google at all. Errors for single products are on their rows.</summary>
    public string LastSyncError { get; set; }

    public bool IsApi => SyncMode == (int)FeedSyncMode.Api;
}

public enum FeedSyncMode
{
    File = 0,
    Api = 1
}

/// <summary>A product that changed and needs sending. Product ID 0 asks for every product.</summary>
public class MerchantSyncQueueItem : BaseEntity
{
    public int ProductId { get; set; }
    public DateTime QueuedOnUtc { get; set; }
}

/// <summary>One item the plugin sent to Google through the API (or tried to), per feed.</summary>
public class MerchantSyncedItem : BaseEntity
{
    public int FeedId { get; set; }
    public int ProductId { get; set; }
    public int? CombinationId { get; set; }
    public string OfferId { get; set; }

    /// <summary>Language and feed label it was sent with, needed to delete it again.</summary>
    public string ContentLanguage { get; set; }
    public string FeedLabel { get; set; }

    /// <summary>Hash of what was last accepted. Empty after an error, so it's sent again.</summary>
    public string Hash { get; set; }

    /// <summary>Google has accepted it at least once, so it must be deleted from Google when it leaves the feed.</summary>
    public bool Live { get; set; }
    public DateTime? SentOnUtc { get; set; }
    public DateTime LastAttemptOnUtc { get; set; }

    /// <summary>Google's reason when the last send failed, otherwise null.</summary>
    public string Error { get; set; }
}

/// <summary>
/// A product as Merchant Center last reported it (release 3), from any data source: the plugin's API source or a feed
/// file. Replaced on every status read.
/// </summary>
public class MerchantGoogleStatus : BaseEntity
{
    /// <summary>The nopCommerce product, or 0 when the offer ID matches none (an item from another system, say).</summary>
    public int ProductId { get; set; }
    public int? CombinationId { get; set; }
    public string OfferId { get; set; }
    public string ContentLanguage { get; set; }
    public string FeedLabel { get; set; }

    /// <summary><see cref="Cartenvo.MerchantCenter.Core.MerchantApi.GoogleStatus"/> as an int.</summary>
    public int Status { get; set; }

    /// <summary>Comma-separated country codes.</summary>
    public string ApprovedCountries { get; set; }
    public string DisapprovedCountries { get; set; }

    /// <summary>JSON list of <see cref="Cartenvo.MerchantCenter.Core.MerchantApi.GoogleIssue"/>.</summary>
    public string IssuesJson { get; set; }

    /// <summary>Google's issue codes, comma-separated, for filtering without reading the JSON.</summary>
    public string IssueCodes { get; set; }

    /// <summary>The product came from the plugin's own API data source.</summary>
    public bool FromApi { get; set; }
    public DateTime? ExpiresOnUtc { get; set; }
    public DateTime ReadOnUtc { get; set; }
}

/// <summary>Which Google product category a store category belongs to.</summary>
public class MerchantCategoryMapping : BaseEntity
{
    public int CategoryId { get; set; }
    public int GoogleCategoryId { get; set; }
}

/// <summary>A problem found in the last generation of a feed. Replaced every time the feed is built.</summary>
public class MerchantFeedIssue : BaseEntity
{
    public int FeedId { get; set; }
    public int ProductId { get; set; }
    public int? CombinationId { get; set; }
    public string OfferId { get; set; }
    public string Code { get; set; }
    public int Severity { get; set; }
}

/// <summary>Per-product choices that nopCommerce has no field for.</summary>
public class MerchantProductSetting : BaseEntity
{
    public int ProductId { get; set; }
    public bool Excluded { get; set; }

    /// <summary>Empty (new), used or refurbished.</summary>
    public string Condition { get; set; }
}
