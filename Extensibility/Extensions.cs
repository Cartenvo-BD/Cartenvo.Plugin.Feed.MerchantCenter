using Cartenvo.MerchantCenter.Core;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Nop.Core.Domain.Catalog;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Extensibility;

/// <summary>
/// Hook points for other plugins, such as Cartenvo's Pro add-on. Register implementations in your own INopStartup.
/// These interfaces are a public contract: members are only ever added, with default implementations, and
/// <see cref="ExtensionApi.Version"/> goes up when they are.
/// </summary>
public static class ExtensionApi
{
    /// <summary>1: item extensions and admin tabs (release 2).</summary>
    public const int Version = 1;
}

/// <summary>
/// Changes products before they're checked and sent, in the feed file, the API sync and the admin preview alike.
/// Errors are logged and skipped: the product is sent without that extension's changes.
/// </summary>
public interface IFeedItemExtension
{
    /// <summary>Lower runs first.</summary>
    int Order => 0;

    /// <summary>Once per run, before any product. Store run-wide data in <see cref="FeedContext.Extensions"/>.</summary>
    Task PrepareAsync(FeedContext context) => Task.CompletedTask;

    /// <summary>Once per page of up to 500 products, to load their data in a few queries.</summary>
    Task PreparePageAsync(FeedContext context, IReadOnlyList<Product> page) => Task.CompletedTask;

    /// <summary>For each item: the product itself, or one of its attribute combinations (see <see cref="FeedItem.CombinationId"/>).</summary>
    Task ApplyAsync(Product product, FeedItem item, FeedContext context);
}

/// <summary>A tab in the plugin's admin pages.</summary>
/// <param name="Key">Pages set <c>ViewData["McTab"]</c> to this key to mark the tab active.</param>
/// <param name="AfterKey">The tab it follows: overview, issues, categories, preview, connection or settings.</param>
public sealed record AdminTab(string Key, string Controller, string Action, string Icon, string Text, string AfterKey = "connection");

public interface IAdminTabProvider
{
    Task<IReadOnlyList<AdminTab>> GetTabsAsync();
}
