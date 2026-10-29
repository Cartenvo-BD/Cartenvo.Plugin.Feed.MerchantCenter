using System.Security.Cryptography;
using System.Text.Json;
using Nop.Data;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Reads and writes the plugin's own tables.</summary>
public class MerchantFeedService(IRepository<MerchantFeed> feeds,
    IRepository<MerchantCategoryMapping> mappings,
    IRepository<MerchantFeedIssue> issues,
    IRepository<MerchantProductSetting> productSettings)
{
    public async Task<IList<MerchantFeed>> GetFeedsAsync()
        => await feeds.GetAllAsync(query => query.OrderBy(feed => feed.Id), getCacheKey: null);

    public Task<MerchantFeed> GetFeedAsync(int id) => feeds.GetByIdAsync(id);

    public async Task InsertFeedAsync(MerchantFeed feed)
    {
        feed.Token ??= NewToken();
        feed.CreatedOnUtc = DateTime.UtcNow;
        await feeds.InsertAsync(feed, false);
    }

    public Task UpdateFeedAsync(MerchantFeed feed) => feeds.UpdateAsync(feed, false);

    public async Task DeleteFeedAsync(MerchantFeed feed)
    {
        await issues.DeleteAsync(issue => issue.FeedId == feed.Id);
        await feeds.DeleteAsync(feed, false);
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static IReadOnlyDictionary<string, int> IssueSummary(MerchantFeed feed)
    {
        if (string.IsNullOrEmpty(feed?.IssueSummaryJson))
            return new Dictionary<string, int>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(feed.IssueSummaryJson) ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    // Category mappings

    public async Task<Dictionary<int, int>> GetCategoryMappingsAsync()
        => (await mappings.GetAllAsync(query => query, getCacheKey: null))
            .GroupBy(mapping => mapping.CategoryId)
            .ToDictionary(group => group.Key, group => group.First().GoogleCategoryId);

    /// <summary>Saves a mapping. A Google ID of 0 removes it.</summary>
    public async Task SetCategoryMappingAsync(int categoryId, int googleCategoryId)
    {
        var existing = await mappings.Table.Where(mapping => mapping.CategoryId == categoryId).ToListAsync();
        if (googleCategoryId <= 0)
        {
            if (existing.Count > 0)
                await mappings.DeleteAsync(existing, false);
            return;
        }
        if (existing.Count == 0)
        {
            await mappings.InsertAsync(new MerchantCategoryMapping { CategoryId = categoryId, GoogleCategoryId = googleCategoryId }, false);
            return;
        }
        existing[0].GoogleCategoryId = googleCategoryId;
        await mappings.UpdateAsync(existing[0], false);
        if (existing.Count > 1)
            await mappings.DeleteAsync(existing.Skip(1).ToList(), false);
    }

    // Product settings

    public async Task<Dictionary<int, MerchantProductSetting>> GetProductSettingsAsync()
        => (await productSettings.GetAllAsync(query => query, getCacheKey: null))
            .GroupBy(setting => setting.ProductId)
            .ToDictionary(group => group.Key, group => group.First());

    public async Task<MerchantProductSetting> GetProductSettingAsync(int productId)
        => await productSettings.Table.FirstOrDefaultAsync(setting => setting.ProductId == productId);

    public async Task SaveProductSettingAsync(int productId, bool excluded, string condition)
    {
        var setting = await GetProductSettingAsync(productId);
        condition = condition is "used" or "refurbished" ? condition : null;
        if (setting == null)
        {
            if (!excluded && condition == null)
                return;
            await productSettings.InsertAsync(new MerchantProductSetting { ProductId = productId, Excluded = excluded, Condition = condition }, false);
            return;
        }
        if (!excluded && condition == null)
        {
            await productSettings.DeleteAsync(setting, false);
            return;
        }
        setting.Excluded = excluded;
        setting.Condition = condition;
        await productSettings.UpdateAsync(setting, false);
    }

    public Task<int> CountExcludedAsync() => productSettings.Table.CountAsync(setting => setting.Excluded);

    // Issues

    public async Task ReplaceIssuesAsync(int feedId, IList<MerchantFeedIssue> found)
    {
        await issues.DeleteAsync(issue => issue.FeedId == feedId);
        foreach (var chunk in found.Chunk(1000))
            await issues.InsertAsync(chunk.ToList(), false);
    }

    /// <summary>Replaces the problems of some products only, after an API sync of changed products.</summary>
    public async Task ReplaceProductIssuesAsync(int feedId, IList<int> productIds, IList<MerchantFeedIssue> found)
    {
        foreach (var chunk in productIds.Chunk(1000))
        {
            var ids = chunk.ToList();
            await issues.DeleteAsync(issue => issue.FeedId == feedId && ids.Contains(issue.ProductId));
        }
        foreach (var chunk in found.Chunk(1000))
            await issues.InsertAsync(chunk.ToList(), false);
    }

    /// <summary>
    /// Held-back and warning counts from the stored problems, after only some products were sent. Counts of
    /// information-only problems (not stored per product) are kept from the last full run.
    /// </summary>
    public async Task RecountIssuesAsync(MerchantFeed feed)
    {
        var rows = await issues.Table.Where(issue => issue.FeedId == feed.Id)
            .Select(issue => new { issue.ProductId, issue.CombinationId, issue.Code, issue.Severity }).ToListAsync();
        var summary = IssueSummary(feed)
            .Where(pair => Cartenvo.MerchantCenter.Core.IssueCodes.Severity.GetValueOrDefault(pair.Key) == Cartenvo.MerchantCenter.Core.IssueSeverity.Info)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var group in rows.GroupBy(row => row.Code))
            summary[group.Key] = group.Count();
        feed.IssueSummaryJson = JsonSerializer.Serialize(summary);
        feed.LastHeldBackCount = rows.Where(row => row.Severity == (int)Cartenvo.MerchantCenter.Core.IssueSeverity.Error)
            .Select(row => (row.ProductId, row.CombinationId)).Distinct().Count();
        var heldBack = rows.Where(row => row.Severity == (int)Cartenvo.MerchantCenter.Core.IssueSeverity.Error)
            .Select(row => (row.ProductId, row.CombinationId)).ToHashSet();
        feed.LastWarningCount = rows.Where(row => row.Severity == (int)Cartenvo.MerchantCenter.Core.IssueSeverity.Warning)
            .Select(row => (row.ProductId, row.CombinationId)).Where(key => !heldBack.Contains(key)).Distinct().Count();
    }

    public IQueryable<MerchantFeedIssue> Issues => issues.Table;

    public Task RemoveProductIssuesAsync(int productId) => issues.DeleteAsync(issue => issue.ProductId == productId);
}
