using Cartenvo.MerchantCenter.Core;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Models;

public record Option(string Value, string Text);

public class FeedCardModel
{
    public MerchantFeed Feed { get; set; }
    public string Url { get; set; }
    public string CountryName { get; set; }
    public string LanguageName { get; set; }
    public string CurrencyCode { get; set; }
    public string StoreName { get; set; }
    public bool FileExists { get; set; }
    public long FileSize { get; set; }
    public IReadOnlyDictionary<string, int> Issues { get; set; }

    /// <summary>API feeds: what Google has from the plugin, and products waiting to be sent.</summary>
    public Cartenvo.Plugin.Feed.MerchantCenter.Services.SyncCounts Sync { get; set; }
    public int Queued { get; set; }

    /// <summary>ready, attention, notbuilt, failed or paused.</summary>
    public string Status
    {
        get
        {
            if (Feed.IsApi)
            {
                if (!string.IsNullOrEmpty(Feed.LastSyncError))
                    return "failed";
                if (Feed.LastFullSyncOnUtc == null)
                    return "notsynced";
                if (!Feed.Enabled)
                    return "paused";
                return Feed.LastHeldBackCount > 0 || Sync?.Errors > 0 ? "attention" : "synced";
            }
            if (!string.IsNullOrEmpty(Feed.LastError))
                return "failed";
            if (Feed.LastGeneratedOnUtc == null || !FileExists)
                return "notbuilt";
            if (!Feed.Enabled)
                return "paused";
            return Feed.LastHeldBackCount > 0 ? "attention" : "ready";
        }
    }

    public int ReadyPercent => Feed.LastItemCount + Feed.LastHeldBackCount == 0 ? 0
        : (int)Math.Round(100.0 * Feed.LastItemCount / (Feed.LastItemCount + Feed.LastHeldBackCount));
}

public class OverviewModel
{
    public bool ApiConnected { get; set; }
    public List<FeedCardModel> Feeds { get; set; } = [];
    public int CategoriesTotal { get; set; }
    public int CategoriesMapped { get; set; }
    public int ExcludedCount { get; set; }
    public Dictionary<string, int> Problems { get; set; } = [];
    public int AutoBuildFeedId { get; set; }
    public FeedEditModel QuickStart { get; set; }

    public int Sent => Feeds.Sum(f => f.Feed.LastItemCount);
    public int HeldBack => Feeds.Sum(f => f.Feed.LastHeldBackCount);
    public int Warnings => Feeds.Sum(f => f.Feed.LastWarningCount);
    public bool AnyBuilt => Feeds.Any(f => f.Feed.LastGeneratedOnUtc != null);
    public int ReadyPercent => Sent + HeldBack == 0 ? 0 : (int)Math.Round(100.0 * Sent / (Sent + HeldBack));
    public int MappedPercent => CategoriesTotal == 0 ? 100 : (int)Math.Round(100.0 * CategoriesMapped / CategoriesTotal);
}

public class CategoryOption
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Depth { get; set; }
    public string Path { get; set; }
}

public class FeedEditModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int StoreId { get; set; }
    public string CountryCode { get; set; }
    public int LanguageId { get; set; }
    public int CurrencyId { get; set; }
    public bool PricesIncludeTax { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllCategories { get; set; } = true;
    public List<int> SelectedCategoryIds { get; set; } = [];
    public bool OnlyInStock { get; set; }
    public decimal? MinPrice { get; set; }
    public string PrimaryCurrencyCode { get; set; }
    public string Url { get; set; }

    /// <summary><see cref="FeedSyncMode"/>: a feed file, or automatic sync through the Merchant API.</summary>
    public int SyncMode { get; set; }
    public bool ApiConnected { get; set; }
    public string ApiAccountName { get; set; }

    public List<Option> Stores { get; set; } = [];
    public List<Option> Countries { get; set; } = [];
    public List<Option> Languages { get; set; } = [];
    public List<Option> Currencies { get; set; } = [];
    public List<CategoryOption> Categories { get; set; } = [];
}

public class IssueRowModel
{
    public int ProductId { get; set; }
    public string Name { get; set; }
    public string Sku { get; set; }
    public string ImageUrl { get; set; }
    public bool Excluded { get; set; }
    public List<(string Code, IssueSeverity Severity, string OfferId)> Problems { get; set; } = [];
}

public class IssuesModel
{
    public List<FeedCardModel> Feeds { get; set; } = [];
    public int FeedId { get; set; }
    public string Code { get; set; }
    public string Search { get; set; }
    public bool ShowExcluded { get; set; }
    public Dictionary<string, int> Counts { get; set; } = [];
    public int ExcludedCount { get; set; }
    public List<IssueRowModel> Rows { get; set; } = [];
    public int Page { get; set; }
    public int TotalProducts { get; set; }
    public int PageSize { get; set; } = 25;
    public bool Truncated { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalProducts / (double)PageSize));
}

public class CategoryRowModel
{
    public int CategoryId { get; set; }
    public string Name { get; set; }
    public string ParentPath { get; set; }
    public int Depth { get; set; }
    public int ProductCount { get; set; }
    public int? GoogleId { get; set; }
    public string GooglePath { get; set; }
    public string InheritedPath { get; set; }
    public int? SuggestedId { get; set; }
    public string SuggestedPath { get; set; }
}

public class CategoriesModel
{
    public List<CategoryRowModel> Rows { get; set; } = [];
    public bool TaxonomyAvailable { get; set; }
    public int Mapped => Rows.Count(r => r.GoogleId != null);
    public int Covered => Rows.Count(r => r.GoogleId != null || r.InheritedPath != null);
    public int Suggestions => Rows.Count(r => r.GoogleId == null && r.SuggestedId != null);
}

public class PreviewItemModel
{
    public FeedItem Item { get; set; }
    public List<FeedIssue> Issues { get; set; }
    public bool HeldBack { get; set; }
    public string Xml { get; set; }
    public string GooglePath { get; set; }
}

public class PreviewModel
{
    public List<FeedCardModel> Feeds { get; set; } = [];
    public int FeedId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public bool InFeed { get; set; }
    public bool Excluded { get; set; }
    public string Condition { get; set; }
    public string StoreName { get; set; }
    public List<PreviewItemModel> Items { get; set; } = [];
}

public class SettingsModel
{
    public int OfferIdSource { get; set; }
    public string DefaultBrand { get; set; }
    public bool NoIdentifiersMeansCustom { get; set; }
    public bool SplitCombinations { get; set; }
    public string ColorAttributeNames { get; set; }
    public string SizeAttributeNames { get; set; }
    public bool UseShortDescriptionFallback { get; set; }
    public int PictureSize { get; set; }
    public int MaxAdditionalImages { get; set; }
    public string DefaultGender { get; set; }
    public string DefaultAgeGroup { get; set; }
    public bool IncludeShippingWeight { get; set; }
    public int RegenerateEveryHours { get; set; }
}

public class SyncErrorModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string OfferId { get; set; }
    public string FeedName { get; set; }
    public string Error { get; set; }
    public DateTime OnUtc { get; set; }

    /// <summary>The feed was deleted or switched to a file, and Google refused the removal.</summary>
    public bool Removing { get; set; }
}

public class ConnectionModel
{
    public string MerchantId { get; set; }
    public string ClientEmail { get; set; }
    public string ProjectId { get; set; }
    public string AccountName { get; set; }
    public bool KeySaved { get; set; }

    /// <summary>False when the saved key can't be decrypted (the store's encryption key changed).</summary>
    public bool KeyReadable { get; set; }
    public bool Registered { get; set; }
    public string DataSource { get; set; }
    public string AdminEmail { get; set; }
    public int Queued { get; set; }
    public List<FeedCardModel> Feeds { get; set; } = [];
    public List<SyncErrorModel> Errors { get; set; } = [];

    public bool Connected => KeySaved && KeyReadable && !string.IsNullOrEmpty(DataSource);

    /// <summary>The step to work on: 1 key, 2 access and registration, 3 connect, 4 done.</summary>
    public int Step => !KeySaved || !KeyReadable ? 1 : !Registered ? 2 : !Connected ? 3 : 4;
}
