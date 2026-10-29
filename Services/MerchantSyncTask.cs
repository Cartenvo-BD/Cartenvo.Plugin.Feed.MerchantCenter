using Nop.Services.ScheduleTasks;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>
/// Runs every few minutes: sends queued product changes, removes products of feeds that stopped syncing, and once a
/// day compares every product with what Google has (which also refreshes products before Google's 30-day expiry).
/// </summary>
public class MerchantSyncTask(MerchantFeedService feedService, MerchantSyncStore store, MerchantSyncService sync,
    MerchantApiFactory apiFactory) : IScheduleTask
{
    /// <summary>Most products sent per run from the queue; the rest wait for the next run.</summary>
    public const int MaxQueuedPerRun = 5000;

    /// <summary>A full comparison asked for by a change (a category renamed, say) runs at most this often.</summary>
    public static readonly TimeSpan RequestedFullSyncGap = TimeSpan.FromHours(1);

    public async Task ExecuteAsync()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        if (!settings.ApiConnected)
            return;

        await sync.RemoveOrphansAsync();

        var feeds = (await feedService.GetFeedsAsync()).Where(feed => feed.IsApi && feed.Enabled).ToList();
        var (upToId, queued) = await store.PeekQueueAsync(MaxQueuedPerRun);
        var everything = queued.Remove(0);
        // More changes than one run sends (an import, say): compare everything instead, which also empties the queue.
        var tooMany = queued.Count >= MaxQueuedPerRun - 1;
        var keepQueue = false;

        foreach (var feed in feeds)
        {
            var now = DateTime.UtcNow;
            var due = feed.LastFullSyncOnUtc == null || now - feed.LastFullSyncOnUtc > MerchantSyncService.FullSyncEvery;
            var requested = tooMany || everything && (feed.LastFullSyncOnUtc == null || now - feed.LastFullSyncOnUtc > RequestedFullSyncGap);
            if (everything && !requested)
                keepQueue = true; // Ran a full sync less than an hour ago; this request waits for the next one.

            SyncResult result;
            if (due || requested)
                result = await sync.SyncAsync(feed);
            else if (queued.Count > 0)
                result = await sync.SyncAsync(feed, queued);
            else
                continue;
            if (result.AlreadyRunning || !result.Success)
                keepQueue = true;
        }

        if (upToId > 0 && feeds.Count > 0 && !keepQueue)
            await store.ClearQueueAsync(upToId);
        else if (upToId > 0 && feeds.Count == 0)
            await store.ClearQueueAsync(upToId);
    }
}
