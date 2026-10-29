using System.Text.Json;
using Cartenvo.MerchantCenter.Core;
using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Data;
using Nop.Services.Configuration;
using Nop.Services.Localization;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

public sealed record StatusReadResult(bool Success, int Products, int Matched, string Error);

public sealed record StatusCounts(int Approved, int Limited, int Disapproved, int Pending)
{
    public int Total => Approved + Limited + Disapproved + Pending;
}

/// <summary>One kind of problem Google reports, with how many products have it.</summary>
public sealed record StatusIssueGroup(string Code, string Description, string Detail, string Documentation, string Attribute,
    string Severity, int Products);

/// <summary>Reads every product's status from Merchant Center and matches it to nopCommerce products.</summary>
public class GoogleStatusService(MerchantApiFactory apiFactory,
    IRepository<MerchantGoogleStatus> statuses,
    IRepository<MerchantSyncedItem> synced,
    IRepository<Product> products,
    IRepository<ProductAttributeCombination> combinations,
    ISettingService settingService,
    ILocalizationService localization)
{
    /// <summary>Stops after this many pages of 1,000 products, so a runaway account can't keep a task busy.</summary>
    public const int MaxPages = 1000;

    private static readonly SemaphoreSlim Running = new(1, 1);
    private static readonly JsonSerializerOptions Json = new();

    public static bool IsRunning => Running.CurrentCount == 0;

    public async Task<StatusReadResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await Running.WaitAsync(0, cancellationToken))
            return new StatusReadResult(false, 0, 0, await TextAsync("Status.AlreadyRunning"));
        try
        {
            var settings = await apiFactory.LoadSettingsAsync();
            var client = settings.ApiCanRead ? apiFactory.Create(settings) : null;
            if (client == null)
                return await FailAsync(settings, await TextAsync("Status.NotConnected"));

            var read = new List<GoogleProductStatus>();
            string pageToken = null;
            for (var page = 0; page < MaxPages; page++)
            {
                var result = await client.ListProductsAsync(1000, pageToken, cancellationToken);
                if (!result.Ok)
                {
                    var why = await TextAsync("Connection.Problem." + result.Problem);
                    return await FailAsync(settings, string.IsNullOrEmpty(result.Message) ? why : $"{why} ({result.Message})");
                }
                if (result.Json?["products"] is System.Text.Json.Nodes.JsonArray list)
                    read.AddRange(list.Select(GoogleStatuses.Parse).Where(p => p != null));
                pageToken = GoogleStatuses.Text(result.Json?["nextPageToken"]);
                if (string.IsNullOrEmpty(pageToken))
                    break;
            }

            var matches = await MatchAsync(read, settings);
            var now = DateTime.UtcNow;
            var rows = read.Select(p =>
            {
                matches.TryGetValue((p.OfferId, p.ContentLanguage, p.FeedLabel), out var match);
                if (match.ProductId == 0)
                    matches.TryGetValue((p.OfferId, "", ""), out match);
                return new MerchantGoogleStatus
                {
                    ProductId = match.ProductId,
                    CombinationId = match.CombinationId,
                    OfferId = Clip(p.OfferId, 150),
                    ContentLanguage = Clip(p.ContentLanguage, 10),
                    FeedLabel = Clip(p.FeedLabel, 50),
                    Status = (int)p.Status,
                    ApprovedCountries = Clip(string.Join(",", p.ApprovedCountries), 1000),
                    DisapprovedCountries = Clip(string.Join(",", p.DisapprovedCountries.Except(p.ApprovedCountries)), 1000),
                    IssuesJson = p.Issues.Count == 0 ? null : JsonSerializer.Serialize(p.Issues, Json),
                    IssueCodes = p.Issues.Count == 0 ? null : Clip(string.Join(",", p.Issues.Select(i => i.Code).Distinct()), 1000),
                    FromApi = !string.IsNullOrEmpty(settings.ApiDataSource) && p.DataSource == settings.ApiDataSource,
                    ExpiresOnUtc = p.ExpiresUtc,
                    ReadOnUtc = now
                };
            }).ToList();

            await statuses.TruncateAsync();
            foreach (var chunk in rows.Chunk(1000))
                await statuses.InsertAsync(chunk.ToList(), false);

            settings.StatusReadOnUtc = now;
            settings.StatusReadError = "";
            await settingService.SaveSettingAsync(settings, s => s.StatusReadOnUtc, clearCache: false);
            await settingService.SaveSettingAsync(settings, s => s.StatusReadError);
            return new StatusReadResult(true, rows.Count, rows.Count(r => r.ProductId > 0), "");
        }
        finally
        {
            Running.Release();
        }
    }

    /// <summary>
    /// Finds the nopCommerce product for each offer ID: first from what the API sync sent, then by working out every
    /// product's offer ID the way the feed does (SKU or product ID, and the combination IDs).
    /// </summary>
    private async Task<Dictionary<(string OfferId, string Language, string Label), (int ProductId, int? CombinationId)>> MatchAsync(
        List<GoogleProductStatus> read, MerchantCenterSettings settings)
    {
        var result = new Dictionary<(string, string, string), (int, int?)>();
        if (read.Count == 0)
            return result;
        var wanted = read.Select(p => p.OfferId).ToHashSet(StringComparer.Ordinal);

        foreach (var item in await synced.Table.Select(s => new { s.OfferId, s.ContentLanguage, s.FeedLabel, s.ProductId, s.CombinationId }).ToListAsync())
            if (wanted.Contains(item.OfferId))
                result.TryAdd((item.OfferId, item.ContentLanguage, item.FeedLabel), (item.ProductId, item.CombinationId));

        var source = (OfferIdSource)settings.OfferIdSource;
        var offerByProduct = new Dictionary<int, string>();
        var lastId = 0;
        while (true)
        {
            // Pages of 50,000 keep memory flat on large catalogs.
            var page = await products.Table.Where(p => !p.Deleted && p.Id > lastId).OrderBy(p => p.Id).Take(50_000)
                .Select(p => new { p.Id, p.Sku }).ToListAsync();
            if (page.Count == 0)
                break;
            foreach (var product in page)
            {
                var offerId = OfferIds.ForProduct(source, product.Id, product.Sku);
                if (offerId == null)
                    continue;
                offerByProduct[product.Id] = offerId;
                if (wanted.Contains(offerId))
                    result.TryAdd((offerId, "", ""), (product.Id, null));
            }
            lastId = page[^1].Id;
        }
        lastId = 0;
        while (true)
        {
            var page = await combinations.Table.Where(c => c.Id > lastId).OrderBy(c => c.Id).Take(50_000)
                .Select(c => new { c.Id, c.ProductId, c.Sku }).ToListAsync();
            if (page.Count == 0)
                break;
            foreach (var combination in page)
            {
                var offerId = OfferIds.ForCombination(source, offerByProduct.GetValueOrDefault(combination.ProductId), combination.Id, combination.Sku);
                if (offerId != null && wanted.Contains(offerId))
                    result.TryAdd((offerId, "", ""), (combination.ProductId, combination.Id));
            }
            lastId = page[^1].Id;
        }
        return result;
    }

    // Reading back

    public async Task<StatusCounts> CountsAsync()
    {
        var byStatus = await statuses.Table.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        int Count(GoogleStatus status) => byStatus.FirstOrDefault(row => row.Key == (int)status)?.Count ?? 0;
        return new StatusCounts(Count(GoogleStatus.Approved), Count(GoogleStatus.Limited), Count(GoogleStatus.Disapproved), Count(GoogleStatus.Pending));
    }

    /// <summary>Problems that need the owner, most products first. Google's texts come from the first product with each issue.</summary>
    public async Task<List<StatusIssueGroup>> IssueGroupsAsync()
    {
        var groups = new Dictionary<string, (GoogleIssue Issue, int Count)>();
        var lastId = 0;
        while (true)
        {
            var page = await statuses.Table.Where(s => s.Id > lastId && s.IssuesJson != null).OrderBy(s => s.Id).Take(5000)
                .Select(s => new { s.Id, s.IssuesJson }).ToListAsync();
            if (page.Count == 0)
                break;
            foreach (var row in page)
                foreach (var issue in Issues(row.IssuesJson).Where(i => i.Resolution != "PENDING_PROCESSING").DistinctBy(i => i.Code))
                    groups[issue.Code] = groups.TryGetValue(issue.Code, out var group) ? (group.Issue, group.Count + 1) : (issue, 1);
            lastId = page[^1].Id;
        }
        return groups.Values
            .OrderByDescending(g => Rank(g.Issue.Severity)).ThenByDescending(g => g.Count)
            .Select(g => new StatusIssueGroup(g.Issue.Code, g.Issue.Description, g.Issue.Detail, g.Issue.Documentation, g.Issue.Attribute,
                g.Issue.Severity, g.Count))
            .ToList();
    }

    public async Task<(List<MerchantGoogleStatus> Rows, int Total)> SearchAsync(int? status, string issueCode, string text, int page, int pageSize)
    {
        var query = statuses.Table;
        if (status is { } wantedStatus)
            query = query.Where(s => s.Status == wantedStatus);
        if (!string.IsNullOrWhiteSpace(issueCode))
        {
            var code = issueCode.Trim();
            query = query.Where(s => s.IssueCodes != null && ("," + s.IssueCodes + ",").Contains("," + code + ","));
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            var term = text.Trim();
            var ids = products.Table.Where(p => p.Name.Contains(term)).Select(p => p.Id);
            query = query.Where(s => s.OfferId.Contains(term) || ids.Contains(s.ProductId));
        }
        var total = await query.CountAsync(CancellationToken.None);
        var rows = await query.OrderByDescending(s => s.Status).ThenBy(s => s.OfferId)
            .Skip(Math.Max(0, page) * pageSize).Take(pageSize).ToListAsync();
        return (rows, total);
    }

    public Task<List<MerchantGoogleStatus>> ForProductAsync(int productId)
        => statuses.Table.Where(s => s.ProductId == productId).OrderBy(s => s.CombinationId).ThenBy(s => s.FeedLabel).ToListAsync();

    public static List<GoogleIssue> Issues(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<GoogleIssue>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int Rank(string severity) => severity switch { "DISAPPROVED" => 3, "DEMOTED" => 2, _ => 1 };

    private async Task<StatusReadResult> FailAsync(MerchantCenterSettings settings, string error)
    {
        settings.StatusReadError = Clip(error, 1000);
        await settingService.SaveSettingAsync(settings, s => s.StatusReadError);
        return new StatusReadResult(false, 0, 0, settings.StatusReadError);
    }

    private Task<string> TextAsync(string key) => localization.GetResourceAsync(MerchantCenterDefaults.ResourcePrefix + key);

    private static string Clip(string value, int max) => value == null ? null : value.Length > max ? value[..max] : value;
}

/// <summary>Reads statuses from Google when they're older than the setting allows. Runs hourly.</summary>
public class GoogleStatusTask(GoogleStatusService service, MerchantApiFactory apiFactory) : Nop.Services.ScheduleTasks.IScheduleTask
{
    public async Task ExecuteAsync()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        if (!settings.ApiCanRead)
            return;
        var every = TimeSpan.FromHours(Math.Clamp(settings.StatusEveryHours, 1, 168));
        if (settings.StatusReadOnUtc is { } last && DateTime.UtcNow - last < every)
            return;
        await service.RefreshAsync();
    }
}
