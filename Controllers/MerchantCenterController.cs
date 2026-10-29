using System.Globalization;
using System.Xml.Linq;
using Cartenvo.MerchantCenter.Core;
using LinqToDB;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Cartenvo.Plugin.Feed.MerchantCenter.Models;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class MerchantCenterController(MerchantFeedService feedService,
    FeedGenerator generator,
    FeedCards cards,
    MerchantSyncService syncService,
    MerchantSyncStore syncStore,
    MerchantApiFactory apiFactory,
    FeedBuilder builder,
    GoogleTaxonomyService taxonomy,
    IStoreService storeService,
    IStoreContext storeContext,
    IWorkContext workContext,
    ICountryService countryService,
    ILanguageService languageService,
    ICurrencyService currencyService,
    CurrencySettings currencySettings,
    ILocalizationService localization,
    INotificationService notifications,
    ISettingService settingService,
    IProductService productService,
    IPictureService pictureService,
    IRepository<Category> categoryRepository,
    IRepository<ProductCategory> productCategoryRepository,
    IRepository<Product> productRepository) : BasePluginController
{
    private const string P = MerchantCenterDefaults.ResourcePrefix;
    private const string Views = MerchantCenterDefaults.ViewPath;

    // Overview

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Configure(int build = 0)
    {
        var feeds = await feedService.GetFeedsAsync();
        var model = new OverviewModel
        {
            AutoBuildFeedId = feeds.Any(feed => feed.Id == build) ? build : 0,
            ApiConnected = (await apiFactory.LoadSettingsAsync()).ApiConnected
        };
        foreach (var feed in feeds)
            model.Feeds.Add(await cards.CardAsync(feed));
        foreach (var card in model.Feeds)
            foreach (var (code, count) in card.Issues)
                model.Problems[code] = model.Problems.GetValueOrDefault(code) + count;

        var (total, mapped) = await CategoryCoverageAsync();
        model.CategoriesTotal = total;
        model.CategoriesMapped = mapped;
        model.ExcludedCount = await feedService.CountExcludedAsync();
        if (feeds.Count == 0)
            model.QuickStart = await PrepareFeedModelAsync(new FeedEditModel(), isNew: true);
        return View(Views + "Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> QuickCreate(FeedEditModel model)
    {
        var feed = new MerchantFeed { Enabled = true };
        await ApplyAsync(feed, model);
        await feedService.InsertFeedAsync(feed);
        return RedirectToAction(nameof(Configure), new { build = feed.Id });
    }

    // Feed editing

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Feed(int id = 0)
    {
        var model = new FeedEditModel();
        if (id > 0)
        {
            var feed = await feedService.GetFeedAsync(id);
            if (feed == null)
                return RedirectToAction(nameof(Configure));
            model = new FeedEditModel
            {
                Id = feed.Id, Name = feed.Name, StoreId = feed.StoreId, CountryCode = feed.CountryCode,
                LanguageId = feed.LanguageId, CurrencyId = feed.CurrencyId, PricesIncludeTax = feed.PricesIncludeTax,
                Enabled = feed.Enabled, OnlyInStock = feed.OnlyInStock, MinPrice = feed.MinPrice, SyncMode = feed.SyncMode,
                SelectedCategoryIds = FeedBuilder.ParseIds(feed.CategoryIds).ToList(),
                Url = await cards.FeedUrlAsync(feed)
            };
            model.AllCategories = model.SelectedCategoryIds.Count == 0;
        }
        return View(Views + "Feed.cshtml", await PrepareFeedModelAsync(model, id == 0));
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Feed(FeedEditModel model, bool build = false)
    {
        MerchantFeed feed;
        if (model.Id > 0)
        {
            feed = await feedService.GetFeedAsync(model.Id);
            if (feed == null)
                return RedirectToAction(nameof(Configure));
            await ApplyAsync(feed, model);
            await feedService.UpdateFeedAsync(feed);
        }
        else
        {
            feed = new MerchantFeed();
            await ApplyAsync(feed, model);
            await feedService.InsertFeedAsync(feed);
        }
        notifications.SuccessNotification(await localization.GetResourceAsync(P + "Saved"));
        return RedirectToAction(nameof(Configure), new { build = build ? feed.Id : 0 });
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> DeleteFeed(int id)
    {
        if (await feedService.GetFeedAsync(id) is { } feed)
        {
            generator.DeleteFile(feed);
            await feedService.DeleteFeedAsync(feed);
            notifications.SuccessNotification(await localization.GetResourceAsync(P + "Deleted"));
        }
        return RedirectToAction(nameof(Configure));
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> NewToken(int id)
    {
        if (await feedService.GetFeedAsync(id) is { } feed)
        {
            feed.Token = MerchantFeedService.NewToken();
            feed.AddedToMerchantCenter = false;
            await feedService.UpdateFeedAsync(feed);
            notifications.SuccessNotification(await localization.GetResourceAsync(P + "Feed.NewToken.Done"));
        }
        return RedirectToAction(nameof(Configure));
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> MarkAdded(int id, bool added = true)
    {
        if (await feedService.GetFeedAsync(id) is { } feed)
        {
            feed.AddedToMerchantCenter = added;
            await feedService.UpdateFeedAsync(feed);
        }
        return RedirectToAction(nameof(Configure));
    }

    // Building

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Generate(int id)
    {
        var feed = await feedService.GetFeedAsync(id);
        if (feed == null)
            return Json(new { ok = false, message = "Not found" });
        if (feed.IsApi)
            return await SyncNowAsync(feed);
        var result = await generator.GenerateAsync(feed);
        string message;
        if (result.AlreadyRunning)
            message = await localization.GetResourceAsync(P + "Busy");
        else if (!result.Success)
            message = await localization.GetResourceAsync(P + "Failed");
        else
            message = string.Format(await localization.GetResourceAsync(P + "Generated"), result.Items.ToString("N0"), result.HeldBack.ToString("N0"));
        return Json(new { ok = result.Success, busy = result.AlreadyRunning, message, items = result.Items, heldBack = result.HeldBack });
    }

    private async Task<IActionResult> SyncNowAsync(MerchantFeed feed)
    {
        var result = await syncService.SyncAsync(feed);
        string message;
        if (result.AlreadyRunning)
            message = await localization.GetResourceAsync(P + "Busy");
        else if (!result.Success)
            message = string.Format(await localization.GetResourceAsync(P + "Sync.Stopped"), result.Error);
        else
            message = string.Format(await localization.GetResourceAsync(P + "Sync.Done"), result.Sent.ToString("N0"),
                result.Unchanged.ToString("N0"), result.Deleted.ToString("N0"), result.Failed.ToString("N0"));
        return Json(new { ok = result.Success, busy = result.AlreadyRunning, message, items = result.Sent + result.Unchanged, heldBack = result.HeldBack });
    }

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public IActionResult Progress(int id)
    {
        var progress = FeedGenerator.GetProgress(id);
        return Json(progress == null
            ? new { running = false, total = 0, processed = 0, items = 0, heldBack = 0, failed = 0 }
            : new { running = progress.Running, total = progress.Total, processed = progress.Processed, items = progress.Items, heldBack = progress.HeldBack, failed = progress.Failed });
    }

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Download(int id)
    {
        var feed = await feedService.GetFeedAsync(id);
        var path = feed == null ? null : generator.FeedFilePath(feed);
        if (path == null || !System.IO.File.Exists(path))
            return RedirectToAction(nameof(Configure));
        return PhysicalFile(path, "application/xml", $"google-feed-{feed.CountryCode.ToLowerInvariant()}-{feed.Id}.xml");
    }

    // Products to fix

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Issues(int feedId = 0, string code = null, string q = null, bool excluded = false, int page = 1)
    {
        var feeds = await feedService.GetFeedsAsync();
        var model = new IssuesModel { Code = code, Search = q?.Trim(), ShowExcluded = excluded, Page = Math.Max(1, page) };
        foreach (var feed in feeds)
            model.Feeds.Add(await cards.CardAsync(feed));
        var selected = feeds.FirstOrDefault(feed => feed.Id == feedId) ?? feeds.FirstOrDefault();
        model.FeedId = selected?.Id ?? 0;
        model.ExcludedCount = await feedService.CountExcludedAsync();
        if (selected != null)
            foreach (var (issueCode, count) in MerchantFeedService.IssueSummary(selected))
                if (IssueCodes.Severity.TryGetValue(issueCode, out var severity) && severity != IssueSeverity.Info)
                    model.Counts[issueCode] = count;

        var settings = await feedService.GetProductSettingsAsync();
        IQueryable<int> productIds;
        if (excluded)
        {
            var ids = settings.Values.Where(s => s.Excluded).Select(s => s.ProductId).ToList();
            productIds = productRepository.Table.Where(p => ids.Contains(p.Id) && !p.Deleted).Select(p => p.Id);
        }
        else if (selected == null)
            productIds = Enumerable.Empty<int>().AsQueryable();
        else
        {
            var issues = feedService.Issues.Where(issue => issue.FeedId == selected.Id);
            if (!string.IsNullOrEmpty(code))
                issues = issues.Where(issue => issue.Code == code);
            productIds = issues.Select(issue => issue.ProductId).Distinct();
            model.Truncated = await feedService.Issues.Where(issue => issue.FeedId == selected.Id).CountAsync(CancellationToken.None) >= MerchantCenterDefaults.MaxStoredIssues;
        }
        if (!string.IsNullOrEmpty(model.Search))
        {
            var term = model.Search;
            var matching = productRepository.Table.Where(p => p.Name.Contains(term) || p.Sku == term).Select(p => p.Id);
            productIds = productIds.Where(id => matching.Contains(id));
        }

        if (selected != null || excluded)
        {
            model.TotalProducts = await productIds.CountAsync(CancellationToken.None);
            var pageIds = await productIds.OrderBy(id => id).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToListAsync();
            var problems = selected == null || excluded ? [] : await feedService.Issues
                .Where(issue => issue.FeedId == selected.Id && pageIds.Contains(issue.ProductId)).ToListAsync();
            foreach (var product in await productService.GetProductsByIdsAsync(pageIds.ToArray()))
            {
                var row = new IssueRowModel
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    Sku = product.Sku,
                    Excluded = settings.TryGetValue(product.Id, out var setting) && setting.Excluded,
                    ImageUrl = await ThumbnailAsync(product.Id)
                };
                foreach (var problem in problems.Where(p => p.ProductId == product.Id)
                    .OrderByDescending(p => p.Severity).ThenBy(p => p.Code))
                    if (!row.Problems.Any(existing => existing.Code == problem.Code))
                        row.Problems.Add((problem.Code, (IssueSeverity)problem.Severity, problem.OfferId));
                model.Rows.Add(row);
            }
            model.Rows = model.Rows.OrderBy(row => pageIds.IndexOf(row.ProductId)).ToList();
        }
        return View(Views + "Issues.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Exclude(int productId, bool exclude)
    {
        var setting = await feedService.GetProductSettingAsync(productId);
        await feedService.SaveProductSettingAsync(productId, exclude, setting?.Condition);
        await syncStore.QueueAsync(productId);
        return Json(new { ok = true, excluded = exclude });
    }

    // Categories

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Categories()
    {
        var index = await taxonomy.GetAsync();
        var model = new CategoriesModel { TaxonomyAvailable = index != null };
        var mappings = await feedService.GetCategoryMappingsAsync();
        var counts = (await productCategoryRepository.Table.GroupBy(pc => pc.CategoryId)
                .Select(group => new { group.Key, Count = group.Count() }).ToListAsync())
            .ToDictionary(row => row.Key, row => row.Count);
        var options = await CategoryOptionsAsync();
        var byId = options.ToDictionary(option => option.Id);
        var parents = (await categoryRepository.Table.Where(c => !c.Deleted).Select(c => new { c.Id, c.ParentCategoryId }).ToListAsync())
            .ToDictionary(c => c.Id, c => c.ParentCategoryId);

        foreach (var option in options)
        {
            var row = new CategoryRowModel
            {
                CategoryId = option.Id,
                Name = option.Name,
                Depth = option.Depth,
                ParentPath = option.Path.Contains(" > ") ? option.Path[..option.Path.LastIndexOf(" > ", StringComparison.Ordinal)] : null,
                ProductCount = counts.GetValueOrDefault(option.Id)
            };
            if (mappings.TryGetValue(option.Id, out var google))
            {
                row.GoogleId = google;
                row.GooglePath = index?.Get(google)?.Path ?? $"#{google}";
            }
            else
            {
                // Nearest mapped parent
                var parent = parents.GetValueOrDefault(option.Id);
                for (var depth = 0; parent != 0 && depth < 20; depth++)
                {
                    if (mappings.TryGetValue(parent, out var inherited))
                    {
                        row.InheritedPath = index?.Get(inherited)?.Path ?? $"#{inherited}";
                        break;
                    }
                    parent = parents.GetValueOrDefault(parent);
                }
                var parentNames = row.ParentPath?.Split(" > ") ?? [];
                if (index?.Suggest(option.Name, parentNames) is { } suggestion)
                {
                    row.SuggestedId = suggestion.Category.Id;
                    row.SuggestedPath = suggestion.Category.Path;
                }
            }
            model.Rows.Add(row);
        }
        return View(Views + "Categories.cshtml", model);
    }

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> TaxonomySearch(string q)
    {
        var index = await taxonomy.GetAsync();
        if (index == null)
            return Json(new { available = false, results = Array.Empty<object>() });
        return Json(new
        {
            available = true,
            results = index.Search(q, 15).Select(category => new { id = category.Id, name = category.Name, path = category.Path })
        });
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> MapCategory(int categoryId, int googleId)
    {
        var index = await taxonomy.GetAsync();
        if (googleId > 0 && index != null && index.Get(googleId) == null)
            return Json(new { ok = false, message = await localization.GetResourceAsync(P + "Categories.UnknownId") });
        await feedService.SetCategoryMappingAsync(categoryId, googleId);
        await syncStore.QueueEverythingAsync();
        return Json(new { ok = true, path = googleId > 0 ? index?.Get(googleId)?.Path ?? $"#{googleId}" : null });
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> AcceptSuggestions()
    {
        var index = await taxonomy.GetAsync();
        if (index == null)
            return RedirectToAction(nameof(Categories));
        var mappings = await feedService.GetCategoryMappingsAsync();
        var accepted = 0;
        foreach (var option in await CategoryOptionsAsync())
        {
            if (mappings.ContainsKey(option.Id))
                continue;
            var parentNames = option.Path.Contains(" > ") ? option.Path[..option.Path.LastIndexOf(" > ", StringComparison.Ordinal)].Split(" > ") : [];
            if (index.Suggest(option.Name, parentNames) is { } suggestion)
            {
                await feedService.SetCategoryMappingAsync(option.Id, suggestion.Category.Id);
                accepted++;
            }
        }
        if (accepted > 0)
            await syncStore.QueueEverythingAsync();
        notifications.SuccessNotification(string.Format(await localization.GetResourceAsync(P + "Categories.Accepted"), accepted));
        return RedirectToAction(nameof(Categories));
    }

    // Preview

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Preview(int feedId = 0, int productId = 0)
    {
        var feeds = await feedService.GetFeedsAsync();
        var model = new PreviewModel { ProductId = productId };
        foreach (var feed in feeds)
            model.Feeds.Add(await cards.CardAsync(feed));
        var selected = feeds.FirstOrDefault(feed => feed.Id == feedId) ?? feeds.FirstOrDefault();
        model.FeedId = selected?.Id ?? 0;
        var product = productId > 0 ? await productService.GetProductByIdAsync(productId) : null;
        if (selected == null || product == null || product.Deleted)
            return View(Views + "Preview.cshtml", model);

        model.ProductName = product.Name;
        var setting = await feedService.GetProductSettingAsync(product.Id);
        model.Excluded = setting?.Excluded == true;
        model.Condition = setting?.Condition ?? "new";

        var context = await builder.PrepareAsync(selected);
        model.StoreName = context.Store.Name;
        var query = await builder.QueryAsync(context);
        model.InFeed = await query.AnyAsync(p => p.Id == product.Id);
        await builder.LoadPageAsync(context, [product]);
        var index = await taxonomy.GetAsync();
        foreach (var (item, issues) in await builder.BuildAsync(product, context))
        {
            model.Items.Add(new PreviewItemModel
            {
                Item = item,
                Issues = issues,
                HeldBack = FeedChecks.IsHeldBack(issues),
                Xml = ItemXml(item),
                GooglePath = item.GoogleProductCategory is { } google ? index?.Get(google)?.Path ?? $"#{google}" : null
            });
        }
        return View(Views + "Preview.cshtml", model);
    }

    private static string ItemXml(FeedItem item)
    {
        using var stream = new MemoryStream();
        using (var writer = new FeedXmlWriter(stream, "preview", "https://example.com/", "preview"))
            writer.Write(item);
        stream.Position = 0;
        var element = XDocument.Load(stream).Root?.Element("channel")?.Element("item");
        return element?.ToString().Replace($" xmlns:g=\"{FeedXmlWriter.GoogleNamespace}\"", "") ?? "";
    }

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> ProductSearch(string q)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q))
            return Json(Array.Empty<object>());
        var found = await productRepository.Table
            .Where(p => !p.Deleted && (p.Name.Contains(q) || p.Sku.Contains(q) || p.Gtin == q))
            .OrderBy(p => p.Name).Take(10)
            .Select(p => new { p.Id, p.Name, p.Sku }).ToListAsync();
        var results = new List<object>();
        foreach (var product in found)
            results.Add(new { id = product.Id, name = product.Name, sku = product.Sku, image = await ThumbnailAsync(product.Id) });
        return Json(results);
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> ProductSetting(int productId, int feedId, bool excluded, string condition)
    {
        await feedService.SaveProductSettingAsync(productId, excluded, condition);
        await syncStore.QueueAsync(productId);
        notifications.SuccessNotification(await localization.GetResourceAsync(P + "Saved"));
        return RedirectToAction(nameof(Preview), new { feedId, productId });
    }

    // Settings

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Settings()
    {
        var settings = await settingService.LoadSettingAsync<MerchantCenterSettings>();
        return View(Views + "Settings.cshtml", new SettingsModel
        {
            OfferIdSource = settings.OfferIdSource,
            DefaultBrand = settings.DefaultBrand,
            NoIdentifiersMeansCustom = settings.NoIdentifiersMeansCustom,
            SplitCombinations = settings.SplitCombinations,
            ColorAttributeNames = settings.ColorAttributeNames,
            SizeAttributeNames = settings.SizeAttributeNames,
            UseShortDescriptionFallback = settings.UseShortDescriptionFallback,
            PictureSize = settings.PictureSize,
            MaxAdditionalImages = settings.MaxAdditionalImages,
            DefaultGender = settings.DefaultGender,
            DefaultAgeGroup = settings.DefaultAgeGroup,
            IncludeShippingWeight = settings.IncludeShippingWeight,
            RegenerateEveryHours = settings.RegenerateEveryHours
        });
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Settings(SettingsModel model)
    {
        var settings = await settingService.LoadSettingAsync<MerchantCenterSettings>();
        settings.OfferIdSource = Enum.IsDefined(typeof(OfferIdSource), model.OfferIdSource) ? model.OfferIdSource : 0;
        settings.DefaultBrand = model.DefaultBrand?.Trim() ?? "";
        settings.NoIdentifiersMeansCustom = model.NoIdentifiersMeansCustom;
        settings.SplitCombinations = model.SplitCombinations;
        settings.ColorAttributeNames = model.ColorAttributeNames?.Trim() ?? "";
        settings.SizeAttributeNames = model.SizeAttributeNames?.Trim() ?? "";
        settings.UseShortDescriptionFallback = model.UseShortDescriptionFallback;
        settings.PictureSize = Math.Clamp(model.PictureSize, 100, 4000);
        settings.MaxAdditionalImages = Math.Clamp(model.MaxAdditionalImages, 0, FeedChecks.MaxAdditionalImages);
        settings.DefaultGender = model.DefaultGender is "male" or "female" or "unisex" ? model.DefaultGender : "";
        settings.DefaultAgeGroup = model.DefaultAgeGroup is "newborn" or "infant" or "toddler" or "kids" or "adult" ? model.DefaultAgeGroup : "";
        settings.IncludeShippingWeight = model.IncludeShippingWeight;
        settings.RegenerateEveryHours = Math.Clamp(model.RegenerateEveryHours, 1, 168);
        await settingService.SaveSettingAsync(settings);
        await syncStore.QueueEverythingAsync();
        notifications.SuccessNotification(await localization.GetResourceAsync(P + "Settings.Saved"));
        return RedirectToAction(nameof(Settings));
    }

    // Helpers

    private async Task<string> ThumbnailAsync(int productId)
    {
        var picture = (await pictureService.GetPicturesByProductIdAsync(productId, 1)).FirstOrDefault();
        return picture == null
            ? await pictureService.GetDefaultPictureUrlAsync(80)
            : await pictureService.GetPictureUrlAsync(picture.Id, 80);
    }

    private async Task ApplyAsync(MerchantFeed feed, FeedEditModel model)
    {
        var stores = await storeService.GetAllStoresAsync();
        feed.StoreId = stores.Any(s => s.Id == model.StoreId) ? model.StoreId : stores.First().Id;
        var countries = await countryService.GetAllCountriesAsync(showHidden: true);
        var country = countries.FirstOrDefault(c => string.Equals(c.TwoLetterIsoCode, model.CountryCode, StringComparison.OrdinalIgnoreCase))
            ?? countries.FirstOrDefault();
        feed.CountryCode = (country?.TwoLetterIsoCode ?? "US").ToUpperInvariant();
        feed.LanguageId = (await languageService.GetLanguageByIdAsync(model.LanguageId))?.Id
            ?? (await languageService.GetAllLanguagesAsync()).First().Id;
        feed.CurrencyId = (await currencyService.GetCurrencyByIdAsync(model.CurrencyId))?.Id ?? currencySettings.PrimaryStoreCurrencyId;
        feed.PricesIncludeTax = model.PricesIncludeTax;
        feed.Enabled = model.Enabled;
        feed.OnlyInStock = model.OnlyInStock;
        feed.MinPrice = model.MinPrice is > 0 ? model.MinPrice : null;
        var mode = model.SyncMode == (int)FeedSyncMode.Api && (await apiFactory.LoadSettingsAsync()).ApiConnected
            ? (int)FeedSyncMode.Api : (int)FeedSyncMode.File;
        if (mode != feed.SyncMode || mode == (int)FeedSyncMode.Api)
        {
            // A new mode, or filters that may have changed: the next sync compares every product.
            feed.LastFullSyncOnUtc = null;
            feed.LastSyncError = null;
        }
        feed.SyncMode = mode;
        MerchantSyncStore.ForgetApiFeedCheck();
        feed.CategoryIds = model.AllCategories || model.SelectedCategoryIds == null
            ? null
            : string.Join(',', model.SelectedCategoryIds.Where(id => id > 0).Distinct());
        var name = model.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            name = country == null ? feed.CountryCode : await localization.GetLocalizedAsync(country, c => c.Name);
        feed.Name = name.Length > 200 ? name[..200] : name;
    }

    private async Task<FeedEditModel> PrepareFeedModelAsync(FeedEditModel model, bool isNew)
    {
        var currentStore = await storeContext.GetCurrentStoreAsync();
        var language = await workContext.GetWorkingLanguageAsync();
        var primary = await currencyService.GetCurrencyByIdAsync(currencySettings.PrimaryStoreCurrencyId);
        model.PrimaryCurrencyCode = primary?.CurrencyCode;
        var apiSettings = await apiFactory.LoadSettingsAsync();
        model.ApiConnected = apiSettings.ApiConnected;
        model.ApiAccountName = string.IsNullOrEmpty(apiSettings.ApiAccountName) ? apiSettings.ApiMerchantId : apiSettings.ApiAccountName;

        model.Stores = (await storeService.GetAllStoresAsync()).Select(s => new Option(s.Id.ToString(), s.Name)).ToList();
        foreach (var country in await countryService.GetAllCountriesAsync())
            if (!string.IsNullOrEmpty(country.TwoLetterIsoCode))
                model.Countries.Add(new Option(country.TwoLetterIsoCode.ToUpperInvariant(), await localization.GetLocalizedAsync(country, c => c.Name)));
        model.Countries = model.Countries.OrderBy(c => c.Text).ToList();
        model.Languages = (await languageService.GetAllLanguagesAsync()).Select(l => new Option(l.Id.ToString(), l.Name)).ToList();
        model.Currencies = (await currencyService.GetAllCurrenciesAsync())
            .Select(c => new Option(c.Id.ToString(), $"{c.Name} ({c.CurrencyCode})")).ToList();
        model.Categories = await CategoryOptionsAsync();

        if (isNew && model.Id == 0)
        {
            model.StoreId = currentStore.Id;
            model.LanguageId = language.Id;
            model.CurrencyId = currencySettings.PrimaryStoreCurrencyId;
            model.CountryCode = CountryFromCulture(language.LanguageCulture, model.Countries) ?? model.Countries.FirstOrDefault()?.Value ?? "US";
            model.PricesIncludeTax = model.CountryCode is not ("US" or "CA");
            model.Name = model.Countries.FirstOrDefault(c => c.Value == model.CountryCode)?.Text;
        }
        return model;
    }

    private static string CountryFromCulture(string culture, List<Option> countries)
    {
        try
        {
            var region = new RegionInfo(new CultureInfo(culture).Name).TwoLetterISORegionName.ToUpperInvariant();
            return countries.Any(c => c.Value == region) ? region : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Published categories as a tree, parents first, with their full path.</summary>
    private async Task<List<CategoryOption>> CategoryOptionsAsync()
    {
        var all = await categoryRepository.Table.Where(c => !c.Deleted)
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync();
        var ids = all.Select(c => c.Id).ToHashSet();
        var children = all.ToLookup(c => ids.Contains(c.ParentCategoryId) ? c.ParentCategoryId : 0);
        var result = new List<CategoryOption>();
        void Walk(int parentId, int depth, string path)
        {
            if (depth > 20)
                return;
            foreach (var category in children[parentId])
            {
                var full = path == null ? category.Name : $"{path} > {category.Name}";
                result.Add(new CategoryOption { Id = category.Id, Name = category.Name, Depth = depth, Path = full });
                Walk(category.Id, depth + 1, full);
            }
        }
        Walk(0, 0, null);
        return result;
    }

    private async Task<(int Total, int Covered)> CategoryCoverageAsync()
    {
        var mappings = await feedService.GetCategoryMappingsAsync();
        var parents = (await categoryRepository.Table.Where(c => !c.Deleted).Select(c => new { c.Id, c.ParentCategoryId }).ToListAsync())
            .ToDictionary(c => c.Id, c => c.ParentCategoryId);
        var covered = 0;
        foreach (var (id, _) in parents)
        {
            var current = id;
            for (var depth = 0; current != 0 && depth < 20; depth++)
            {
                if (mappings.ContainsKey(current))
                {
                    covered++;
                    break;
                }
                current = parents.GetValueOrDefault(current);
            }
        }
        return (parents.Count, covered);
    }
}
