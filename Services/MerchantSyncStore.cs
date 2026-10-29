using LinqToDB;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Nop.Data;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>The change queue and the record of what was sent to Google.</summary>
public class MerchantSyncStore(IRepository<MerchantSyncQueueItem> queue,
    IRepository<MerchantSyncedItem> synced,
    IRepository<MerchantFeed> feeds)
{
    // Product saves fire many events; this saves a query per event. Refreshed every minute and when feeds change.
    private static DateTime _checkedOnUtc = DateTime.MinValue;
    private static bool _anyApiFeed;

    public static void ForgetApiFeedCheck() => _checkedOnUtc = DateTime.MinValue;

    /// <summary>Whether any feed syncs through the API, so changes need queueing.</summary>
    public async Task<bool> AnyApiFeedAsync()
    {
        if (DateTime.UtcNow - _checkedOnUtc < TimeSpan.FromMinutes(1))
            return _anyApiFeed;
        _anyApiFeed = await feeds.Table.AnyAsync(feed => feed.SyncMode == (int)FeedSyncMode.Api && feed.Enabled);
        _checkedOnUtc = DateTime.UtcNow;
        return _anyApiFeed;
    }

    public async Task QueueAsync(int productId)
    {
        if (productId < 0 || !await AnyApiFeedAsync())
            return;
        await queue.InsertAsync(new MerchantSyncQueueItem { ProductId = productId, QueuedOnUtc = DateTime.UtcNow }, false);
    }

    public async Task QueueAsync(IEnumerable<int> productIds)
    {
        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0 || !await AnyApiFeedAsync())
            return;
        var now = DateTime.UtcNow;
        foreach (var chunk in ids.Chunk(1000))
            await queue.InsertAsync(chunk.Select(id => new MerchantSyncQueueItem { ProductId = id, QueuedOnUtc = now }).ToList(), false);
    }

    /// <summary>Asks for every product to be compared with Google on the next run (settings or category mappings changed).</summary>
    public Task QueueEverythingAsync() => QueueAsync(0);

    /// <summary>Queued product IDs up to a queue row, so rows added while a sync runs stay for the next run.</summary>
    public async Task<(int UpToId, List<int> ProductIds)> PeekQueueAsync(int max)
    {
        var upTo = await queue.Table.OrderByDescending(row => row.Id).Select(row => row.Id).FirstOrDefaultAsync(CancellationToken.None);
        if (upTo == 0)
            return (0, []);
        var ids = await queue.Table.Where(row => row.Id <= upTo).Select(row => row.ProductId).Distinct().Take(max).ToListAsync();
        return (upTo, ids);
    }

    public Task ClearQueueAsync(int upToId) => queue.DeleteAsync(row => row.Id <= upToId);

    public Task<int> CountQueuedAsync() => queue.Table.Select(row => row.ProductId).Distinct().CountAsync(CancellationToken.None);

    // Sent items

    public IQueryable<MerchantSyncedItem> Synced => synced.Table;

    public async Task<List<MerchantSyncedItem>> GetSyncedAsync(int feedId, IList<int> productIds)
    {
        if (productIds == null)
            return await synced.Table.Where(row => row.FeedId == feedId).ToListAsync();
        var result = new List<MerchantSyncedItem>();
        foreach (var chunk in productIds.Chunk(1000))
        {
            var ids = chunk.ToList();
            result.AddRange(await synced.Table.Where(row => row.FeedId == feedId && ids.Contains(row.ProductId)).ToListAsync());
        }
        return result;
    }

    /// <summary>Offer IDs in a feed that belong to other products, to catch duplicates when only some products are sent.</summary>
    public async Task<HashSet<string>> OfferIdsOfOtherProductsAsync(int feedId, IList<int> productIds, IList<string> offerIds)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in offerIds.Distinct().Chunk(1000))
        {
            var offers = chunk.ToList();
            var ids = productIds.ToList();
            result.UnionWith(await synced.Table
                .Where(row => row.FeedId == feedId && offers.Contains(row.OfferId) && !ids.Contains(row.ProductId))
                .Select(row => row.OfferId).ToListAsync());
        }
        return result;
    }

    public async Task SaveAsync(IList<MerchantSyncedItem> inserted, IList<MerchantSyncedItem> updated, IList<MerchantSyncedItem> deleted)
    {
        foreach (var chunk in inserted.Chunk(500))
            await synced.InsertAsync(chunk.ToList(), false);
        foreach (var chunk in updated.Chunk(500))
            await synced.UpdateAsync(chunk.ToList(), false);
        foreach (var chunk in deleted.Chunk(1000))
        {
            var ids = chunk.Select(row => row.Id).ToList();
            await synced.DeleteAsync(row => ids.Contains(row.Id));
        }
    }

    /// <summary>Rows of feeds that no longer sync through the API (deleted or switched to a file), to remove from Google.</summary>
    public async Task<List<MerchantSyncedItem>> GetOrphansAsync(int max)
    {
        var apiFeeds = feeds.Table.Where(feed => feed.SyncMode == (int)FeedSyncMode.Api).Select(feed => feed.Id);
        return await synced.Table.Where(row => !apiFeeds.Contains(row.FeedId)).OrderBy(row => row.Id).Take(max).ToListAsync();
    }

    /// <summary>Forgets everything sent, after the data source changed: Google has none of it in the new one.</summary>
    public Task ForgetAllSentAsync() => synced.DeleteAsync(row => row.Id > 0);

    public async Task<SyncCounts> CountsAsync(int feedId)
    {
        var rows = synced.Table.Where(row => row.FeedId == feedId);
        return new SyncCounts(
            await rows.CountAsync(row => row.Live && row.Error == null),
            await rows.CountAsync(row => row.Error != null),
            await rows.Where(row => row.Error == null).Select(row => row.SentOnUtc).MaxAsync(date => (DateTime?)date));
    }

    public async Task<List<MerchantSyncedItem>> GetErrorsAsync(int feedId, int max)
        => await synced.Table.Where(row => row.Error != null && (feedId == 0 || row.FeedId == feedId))
            .OrderByDescending(row => row.LastAttemptOnUtc).Take(max).ToListAsync();
}

public sealed record SyncCounts(int Live, int Errors, DateTime? LastSentOnUtc);
