using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cartenvo.MerchantCenter.Core;
using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Services.Logging;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

public sealed record SyncResult(bool Success, bool AlreadyRunning, int Sent, int Unchanged, int Deleted, int Failed, int HeldBack,
    int DurationMs, string Error)
{
    public static SyncResult Busy => new(false, true, 0, 0, 0, 0, 0, 0, null);
    public static SyncResult Stopped(string error, int ms) => new(false, false, 0, 0, 0, 0, 0, ms, error);
}

/// <summary>
/// Sends a feed's products to Google through the Merchant API. Builds each product the same way as the feed file, sends
/// only what changed since Google last accepted it (or wasn't refreshed for 25 days), and deletes from Google what left
/// the feed.
/// </summary>
public class MerchantSyncService(FeedBuilder builder,
    MerchantFeedService feedService,
    MerchantSyncStore store,
    MerchantApiFactory apiFactory,
    Nop.Services.Localization.ILocalizationService localization,
    ILogger logger)
{
    /// <summary>Google removes products that aren't refreshed for 30 days.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(25);

    /// <summary>Every product is compared with what was sent once a day, which catches changes no event reported.</summary>
    public static readonly TimeSpan FullSyncEvery = TimeSpan.FromHours(24);

    private sealed record Planned(FeedItem Item, JsonObject Input, string Hash, MerchantSyncedItem Row);

    /// <summary>Syncs the given products, or every product in the feed when <paramref name="productIds"/> is null.</summary>
    public async Task<SyncResult> SyncAsync(MerchantFeed feed, IList<int> productIds = null)
    {
        using var run = await FeedGenerator.TryStartAsync(feed.Id);
        if (run == null)
            return SyncResult.Busy;
        var progress = run.Progress;
        var clock = Stopwatch.StartNew();
        var full = productIds == null;

        var settings = await apiFactory.LoadSettingsAsync();
        var client = settings.ApiConnected ? apiFactory.Create(settings) : null;
        if (client == null)
            return await StopAsync(feed, await localization.GetResourceAsync(MerchantCenterDefaults.ResourcePrefix + "Sync.NotConnected"), clock);

        try
        {
            var context = await builder.PrepareAsync(feed);
            var language = ContentLanguage(context.Language);
            var label = feed.CountryCode.ToUpperInvariant();
            var parallel = Math.Clamp(settings.ApiParallelRequests, 1, 10);

            var query = await builder.QueryAsync(context);
            if (!full)
            {
                var ids = productIds.ToList();
                query = query.Where(p => ids.Contains(p.Id));
            }
            progress.Total = full ? await query.CountAsync(CancellationToken.None) : productIds.Count;

            var existing = await store.GetSyncedAsync(feed.Id, productIds);
            var byOffer = existing.GroupBy(row => row.OfferId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var kept = new HashSet<int>(); // row IDs still in the feed
            var seenOffers = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>();
            var issues = new List<MerchantFeedIssue>();
            int sent = 0, unchanged = 0, failed = 0, heldBack = 0, warnings = 0, deleted = 0;
            var now = DateTime.UtcNow;
            var lastLog = TimeSpan.Zero;
            string stopError = null;
            var sentNames = new HashSet<string>(StringComparer.Ordinal); // offer IDs sent in this run, with this language and label

            for (var pageIndex = 0; stopError == null; pageIndex++)
            {
                var page = await query.Skip(pageIndex * MerchantCenterDefaults.PageSize).Take(MerchantCenterDefaults.PageSize).ToListAsync();
                if (page.Count == 0)
                    break;
                await builder.LoadPageAsync(context, page);

                var built = new List<BuiltItem>();
                foreach (var product in page)
                {
                    try
                    {
                        built.AddRange(await builder.BuildAsync(product, context));
                    }
                    catch (Exception ex)
                    {
                        await logger.WarningAsync($"Merchant Center sync: couldn't build product {product.Id}.", ex);
                    }
                    progress.Processed++;
                }

                // A partial sync can't see the other products' IDs in this run, so it checks the ones already sent.
                var takenElsewhere = full ? null : await store.OfferIdsOfOtherProductsAsync(feed.Id, productIds,
                    built.Select(b => b.Item.Id).Where(id => id.Length > 0).ToList());

                var toSend = new List<Planned>();
                foreach (var (item, itemIssues) in built)
                {
                    if (feed.OnlyInStock && item.Availability == ItemAvailability.OutOfStock)
                        continue;
                    if (item.Id.Length > 0 && (!seenOffers.Add(item.Id) || takenElsewhere?.Contains(item.Id) == true))
                        itemIssues.Add(IssueCodes.Create(IssueCodes.DuplicateId, "id"));

                    foreach (var issue in itemIssues)
                    {
                        counts[issue.Code] = counts.GetValueOrDefault(issue.Code) + 1;
                        if (issue.Severity != IssueSeverity.Info && issues.Count < MerchantCenterDefaults.MaxStoredIssues)
                            issues.Add(new MerchantFeedIssue
                            {
                                FeedId = feed.Id, ProductId = item.ProductId, CombinationId = item.CombinationId,
                                OfferId = item.Id.Length > 100 ? item.Id[..100] : item.Id, Code = issue.Code, Severity = (int)issue.Severity
                            });
                    }
                    if (FeedChecks.IsHeldBack(itemIssues))
                    {
                        heldBack++;
                        continue; // Not kept: if Google has it from before, it's deleted below.
                    }
                    if (itemIssues.Any(issue => issue.Severity == IssueSeverity.Warning))
                        warnings++;

                    var input = ProductInputs.Build(item, language, label);
                    var hash = ProductInputs.Hash(input);
                    byOffer.TryGetValue(item.Id, out var row);
                    // The ID moved to another product, or the feed's language or country changed: send it as a new
                    // item; the old row isn't kept, so it's removed from Google below.
                    if (row != null && (row.ProductId != item.ProductId || row.ContentLanguage != language || row.FeedLabel != label))
                        row = null;
                    if (row != null)
                        kept.Add(row.Id);
                    if (row != null && row.Error == null && row.Hash == hash && row.SentOnUtc > now - RefreshAfter)
                    {
                        unchanged++;
                        continue;
                    }
                    toSend.Add(new Planned(item, input, hash, row));
                }

                var (pageSent, pageFailed, fatal) = await SendAsync(client, settings.ApiDataSource, feed, language, label, toSend, parallel);
                foreach (var planned in toSend)
                    sentNames.Add(planned.Item.Id);
                sent += pageSent;
                failed += pageFailed;
                stopError = fatal;
                progress.Items = sent + unchanged;
                progress.HeldBack = heldBack;
                progress.Failed = failed;

                if (clock.Elapsed - lastLog > TimeSpan.FromMinutes(1))
                {
                    lastLog = clock.Elapsed;
                    await logger.InformationAsync($"Merchant Center sync \"{feed.Name}\": {progress.Processed:N0} of {progress.Total:N0} products after {clock.Elapsed.TotalSeconds:0} s; {sent:N0} sent, {unchanged:N0} unchanged, {failed:N0} failed.");
                }
            }

            if (stopError != null)
                return await StopAsync(feed, stopError, clock);

            // What left the feed: deleted, unpublished, filtered out, held back or given a new ID.
            var gone = existing.Where(row => !kept.Contains(row.Id)).ToList();
            foreach (var row in gone)
                if (row.ContentLanguage == language && row.FeedLabel == label && sentNames.Contains(row.OfferId))
                    row.Live = false; // Sent again in this run for another product: drop the old row, keep it in Google.
            deleted = await DeleteAsync(client, settings.ApiDataSource, gone, parallel);

            if (full)
            {
                await feedService.ReplaceIssuesAsync(feed.Id, issues);
                feed.IssueSummaryJson = JsonSerializer.Serialize(counts);
                feed.LastHeldBackCount = heldBack;
                feed.LastWarningCount = warnings;
                feed.LastFullSyncOnUtc = DateTime.UtcNow;
                feed.LastDurationMs = (int)clock.ElapsedMilliseconds;
                feed.LastGeneratedOnUtc = DateTime.UtcNow;
            }
            else
            {
                await feedService.ReplaceProductIssuesAsync(feed.Id, productIds, issues);
                await feedService.RecountIssuesAsync(feed);
            }
            feed.LastItemCount = (await store.CountsAsync(feed.Id)).Live;
            feed.LastSyncOnUtc = DateTime.UtcNow;
            feed.LastSyncError = null;
            feed.LastError = null;
            await feedService.UpdateFeedAsync(feed);

            if (full || sent + deleted + failed > 0)
                await logger.InformationAsync($"Merchant Center sync \"{feed.Name}\": {sent:N0} sent, {unchanged:N0} unchanged, {deleted:N0} removed, {failed:N0} failed, {heldBack:N0} held back, in {clock.Elapsed.TotalSeconds:0.0} s.");
            return new SyncResult(true, false, sent, unchanged, deleted, failed, heldBack, (int)clock.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            await logger.ErrorAsync($"Merchant Center sync of feed \"{feed.Name}\" failed.", ex);
            return await StopAsync(feed, ex.Message, clock);
        }
    }

    private async Task<SyncResult> StopAsync(MerchantFeed feed, string error, Stopwatch clock)
    {
        feed.LastSyncError = error.Length > 1000 ? error[..1000] : error;
        feed.LastSyncOnUtc = DateTime.UtcNow;
        await feedService.UpdateFeedAsync(feed);
        return SyncResult.Stopped(feed.LastSyncError, (int)clock.ElapsedMilliseconds);
    }

    /// <summary>
    /// Sends items in parallel and records the results. A problem with the connection itself (key, access, API
    /// switched off) stops the run, since every other call would fail the same way.
    /// </summary>
    private async Task<(int Sent, int Failed, string Fatal)> SendAsync(MerchantApiClient client, string dataSource, MerchantFeed feed,
        string language, string label, List<Planned> items, int parallel)
    {
        if (items.Count == 0)
            return (0, 0, null);
        using var gate = new SemaphoreSlim(parallel);
        var results = await Task.WhenAll(items.Select(async planned =>
        {
            await gate.WaitAsync();
            try
            {
                return (planned, result: await client.InsertProductAsync(dataSource, planned.Input));
            }
            finally
            {
                gate.Release();
            }
        }));

        var inserted = new List<MerchantSyncedItem>();
        var updated = new List<MerchantSyncedItem>();
        int sent = 0, failed = 0;
        string fatal = null;
        var retryLater = new HashSet<int>();
        var now = DateTime.UtcNow;
        foreach (var (planned, result) in results)
        {
            // Problems with the connection itself would fail every other call the same way, so the run stops.
            if (fatal == null && result.Problem is ApiProblems.BadKey or ApiProblems.NoAccess or ApiProblems.NotRegistered
                    or ApiProblems.ApiDisabled or ApiProblems.NotFound or ApiProblems.Network)
            {
                fatal = await localization.GetResourceAsync(MerchantCenterDefaults.ResourcePrefix + "Connection.Problem." + result.Problem);
                await logger.WarningAsync($"Merchant Center sync of feed \"{feed.Name}\" stopped. Google said: {result.Message}");
            }
            if (fatal != null && !result.Ok)
                continue; // Not the product's fault; it's sent again on the next run.
            if (result.Problem == ApiProblems.Quota)
            {
                // Google's limit, still reached after retries: send it again next run, without calling it refused.
                retryLater.Add(planned.Item.ProductId);
                failed++;
                continue;
            }

            var row = planned.Row ?? new MerchantSyncedItem { FeedId = feed.Id };
            row.ProductId = planned.Item.ProductId;
            row.CombinationId = planned.Item.CombinationId;
            row.OfferId = planned.Item.Id;
            row.LastAttemptOnUtc = now;
            if (result.Ok)
            {
                row.ContentLanguage = language;
                row.FeedLabel = label;
                row.Hash = planned.Hash;
                row.Live = true;
                row.SentOnUtc = now;
                row.Error = null;
                sent++;
            }
            else
            {
                // A row that was never accepted keeps the language and label it was tried with, for the error list.
                if (!row.Live)
                {
                    row.ContentLanguage = language;
                    row.FeedLabel = label;
                }
                row.Hash = "";
                var message = result.Message ?? result.Problem;
                row.Error = message.Length > 1000 ? message[..1000] : message;
                failed++;
            }
            (row.Id == 0 ? inserted : updated).Add(row);
        }
        await store.SaveAsync(inserted, updated, []);
        if (retryLater.Count > 0)
            await store.QueueAsync(retryLater);
        return (sent, failed, fatal);
    }

    /// <summary>Deletes rows from Google (those it accepted) and from the table. Returns how many were removed.</summary>
    public async Task<int> DeleteAsync(MerchantApiClient client, string dataSource, IList<MerchantSyncedItem> rows, int parallel)
    {
        if (rows.Count == 0)
            return 0;
        using var gate = new SemaphoreSlim(parallel);
        var results = await Task.WhenAll(rows.Select(async row =>
        {
            if (!row.Live)
                return (row, ok: true);
            await gate.WaitAsync();
            try
            {
                var result = await client.DeleteProductAsync(dataSource, row.ContentLanguage, row.FeedLabel, row.OfferId);
                return (row, ok: result.Ok);
            }
            finally
            {
                gate.Release();
            }
        }));
        var removed = results.Where(r => r.ok).Select(r => r.row).ToList();
        var failedRows = results.Where(r => !r.ok).Select(r => r.row).ToList();
        foreach (var row in failedRows)
        {
            row.LastAttemptOnUtc = DateTime.UtcNow;
            row.Error ??= "Couldn't remove this product from Google. It's tried again on the next run.";
        }
        await store.SaveAsync([], failedRows, removed);
        return removed.Count(row => row.Live);
    }

    /// <summary>
    /// Removes from Google the products of feeds that were deleted or switched back to a feed file, so they aren't
    /// listed twice. Returns how many were removed.
    /// </summary>
    public async Task<int> RemoveOrphansAsync()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        var client = settings.ApiConnected ? apiFactory.Create(settings) : null;
        if (client == null)
            return 0;
        var total = 0;
        for (var round = 0; round < 20; round++)
        {
            var rows = await store.GetOrphansAsync(1000);
            if (rows.Count == 0)
                break;
            var removed = await DeleteAsync(client, settings.ApiDataSource, rows, Math.Clamp(settings.ApiParallelRequests, 1, 10));
            total += removed;
            if (rows.All(row => row.Live) && removed == 0)
                break; // Google refuses; try again next run.
        }
        if (total > 0)
            await logger.InformationAsync($"Merchant Center sync: removed {total:N0} products of feeds that no longer sync through the API.");
        return total;
    }

    /// <summary>Google's content language: the two-letter code of the feed's language.</summary>
    public static string ContentLanguage(Nop.Core.Domain.Localization.Language language)
    {
        var code = language?.UniqueSeoCode?.Trim().ToLowerInvariant();
        if (code is { Length: 2 })
            return code;
        try
        {
            return new CultureInfo(language?.LanguageCulture ?? "en-US").TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return "en";
        }
    }
}
