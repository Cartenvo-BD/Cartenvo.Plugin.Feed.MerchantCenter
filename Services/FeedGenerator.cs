using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Cartenvo.MerchantCenter.Core;
using LinqToDB;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Logging;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Progress of a running feed build, polled by the admin page.</summary>
public sealed class FeedProgress
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int Items { get; set; }
    public int HeldBack { get; set; }

    /// <summary>API sync only: items Google refused or that couldn't be sent.</summary>
    public int Failed { get; set; }
    public bool Running { get; set; }
    public DateTime StartedOnUtc { get; set; }
}

public sealed record FeedRunResult(bool Success, bool AlreadyRunning, int Items, int HeldBack, int Warnings, int DurationMs, string Error);

/// <summary>Builds a feed file and records what was held back and why.</summary>
public class FeedGenerator(FeedBuilder builder, MerchantFeedService feedService, INopFileProvider files, ILogger logger)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private static readonly ConcurrentDictionary<int, FeedProgress> Running = new();

    public static FeedProgress GetProgress(int feedId) => Running.GetValueOrDefault(feedId);

    /// <summary>
    /// Starts a run of a feed (a build or an API sync), or returns null when one is already running. Dispose the
    /// result to finish it.
    /// </summary>
    internal static async Task<FeedRun> TryStartAsync(int feedId)
    {
        var gate = Locks.GetOrAdd(feedId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0))
            return null;
        var progress = new FeedProgress { Running = true, StartedOnUtc = DateTime.UtcNow };
        Running[feedId] = progress;
        return new FeedRun(progress, () =>
        {
            progress.Running = false;
            Running.TryRemove(feedId, out _);
            gate.Release();
        });
    }

    internal sealed class FeedRun(FeedProgress progress, Action finish) : IDisposable
    {
        public FeedProgress Progress => progress;
        public void Dispose() => finish();
    }

    public string FeedFilePath(MerchantFeed feed)
        => files.Combine(files.MapPath(MerchantCenterDefaults.DataFolder), $"feed-{feed.Id}.xml");

    public async Task<FeedRunResult> GenerateAsync(MerchantFeed feed)
    {
        using var run = await TryStartAsync(feed.Id);
        if (run == null)
            return new FeedRunResult(false, true, 0, 0, 0, 0, null);

        var progress = run.Progress;
        var clock = Stopwatch.StartNew();
        var path = FeedFilePath(feed);
        var temp = path + ".tmp";
        try
        {
            files.CreateDirectory(files.GetDirectoryName(path));
            var context = await builder.PrepareAsync(feed);
            var query = await builder.QueryAsync(context);
            progress.Total = await query.CountAsync(CancellationToken.None);

            var counts = new Dictionary<string, int>();
            var stored = new List<MerchantFeedIssue>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int items = 0, heldBack = 0, warnings = 0;
            var lastLog = TimeSpan.Zero;

            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var writer = new FeedXmlWriter(stream, context.Store.Name, context.StoreUrl, $"{context.Store.Name} - {feed.Name}", indent: false))
            {
                for (var pageIndex = 0; ; pageIndex++)
                {
                    var step = Stopwatch.StartNew();
                    var page = await query.Skip(pageIndex * MerchantCenterDefaults.PageSize).Take(MerchantCenterDefaults.PageSize).ToListAsync();
                    if (page.Count == 0)
                        break;
                    await builder.LoadPageAsync(context, page);
                    context.Timings.Add("loading", step);
                    foreach (var product in page)
                    {
                        List<BuiltItem> built;
                        try
                        {
                            built = await builder.BuildAsync(product, context);
                        }
                        catch (Exception ex)
                        {
                            await logger.WarningAsync($"Merchant Center feed: couldn't build product {product.Id}.", ex);
                            progress.Processed++;
                            continue;
                        }

                        foreach (var (item, issues) in built)
                        {
                            if (feed.OnlyInStock && item.Availability == ItemAvailability.OutOfStock)
                                continue;
                            if (item.Id.Length > 0 && !seenIds.Add(item.Id))
                                issues.Add(IssueCodes.Create(IssueCodes.DuplicateId, "id"));

                            foreach (var issue in issues)
                            {
                                counts[issue.Code] = counts.GetValueOrDefault(issue.Code) + 1;
                                // Info rows (unmapped categories, out of stock) are counted, not stored per product.
                                if (issue.Severity != IssueSeverity.Info && stored.Count < MerchantCenterDefaults.MaxStoredIssues)
                                    stored.Add(new MerchantFeedIssue
                                    {
                                        FeedId = feed.Id, ProductId = item.ProductId, CombinationId = item.CombinationId,
                                        OfferId = item.Id.Length > 100 ? item.Id[..100] : item.Id, Code = issue.Code, Severity = (int)issue.Severity
                                    });
                            }

                            if (FeedChecks.IsHeldBack(issues))
                            {
                                heldBack++;
                                continue;
                            }
                            if (issues.Any(issue => issue.Severity == IssueSeverity.Warning))
                                warnings++;
                            writer.Write(item);
                            items++;
                        }
                        progress.Processed++;
                        progress.Items = items;
                        progress.HeldBack = heldBack;
                    }

                    // Long builds leave a progress line in the log about once a minute.
                    if (clock.Elapsed - lastLog > TimeSpan.FromMinutes(1))
                    {
                        lastLog = clock.Elapsed;
                        await logger.InformationAsync($"Merchant Center feed \"{feed.Name}\": {progress.Processed:N0} of {progress.Total:N0} products after {clock.Elapsed.TotalSeconds:0} s. Time per step: {context.Timings}.");
                    }
                }
                writer.Complete();
            }

            File.Move(temp, path, true);
            await feedService.ReplaceIssuesAsync(feed.Id, stored);

            feed.LastGeneratedOnUtc = DateTime.UtcNow;
            feed.LastItemCount = items;
            feed.LastHeldBackCount = heldBack;
            feed.LastWarningCount = warnings;
            feed.LastDurationMs = (int)clock.ElapsedMilliseconds;
            feed.LastError = null;
            feed.IssueSummaryJson = JsonSerializer.Serialize(counts);
            await feedService.UpdateFeedAsync(feed);
            return new FeedRunResult(true, false, items, heldBack, warnings, feed.LastDurationMs, null);
        }
        catch (Exception ex)
        {
            await logger.ErrorAsync($"Merchant Center feed: building feed \"{feed.Name}\" failed.", ex);
            if (File.Exists(temp))
                File.Delete(temp);
            feed.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            feed.LastDurationMs = (int)clock.ElapsedMilliseconds;
            await feedService.UpdateFeedAsync(feed);
            return new FeedRunResult(false, false, 0, 0, 0, feed.LastDurationMs, feed.LastError);
        }
    }

    public void DeleteFile(MerchantFeed feed)
    {
        var path = FeedFilePath(feed);
        if (File.Exists(path))
            File.Delete(path);
    }
}
