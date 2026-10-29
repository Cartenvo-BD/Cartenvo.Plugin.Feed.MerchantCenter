using Cartenvo.MerchantCenter.Core;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Seo;
using Nop.Core.Domain.Stores;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Web.Framework.Mvc.Routing;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Everything a feed needs that's the same for every product, loaded once per run.</summary>
public sealed class FeedContext
{
    public MerchantFeed Feed { get; init; }
    public Store Store { get; init; }
    public string StoreUrl { get; init; }
    public Language Language { get; init; }
    public Currency Currency { get; init; }
    public Customer Customer { get; init; }
    public MerchantCenterSettings Settings { get; init; }
    public Dictionary<int, Category> Categories { get; init; }
    public Dictionary<int, string> CategoryNames { get; init; }
    public Dictionary<int, int> Mappings { get; init; }
    public Dictionary<int, string> ManufacturerNames { get; init; }
    public Dictionary<int, MerchantProductSetting> ProductSettings { get; init; }

    /// <summary>Selected categories and everything below them. Null includes all products.</summary>
    public HashSet<int> CategoryFilter { get; init; }
    public string WeightUnit { get; init; }
    public HashSet<string> ColorNames { get; init; }
    public HashSet<string> SizeNames { get; init; }
    public Dictionary<int, string> AttributeNames { get; } = [];

    /// <summary>Time spent in each build step, for the progress log of long builds.</summary>
    public StepTimings Timings { get; } = new();

    /// <summary>Use language-specific URL names (nopCommerce only does when two or more languages are published).</summary>
    public bool LocalizedSlugs { get; init; }

    // Loaded for each page of products: a few queries per page instead of several per product.
    public IDictionary<int, int[]> ProductCategoryIds { get; set; } = new Dictionary<int, int[]>();
    public IDictionary<int, int[]> ProductManufacturerIds { get; set; } = new Dictionary<int, int[]>();
    public ILookup<int, Picture> Pictures { get; set; } = Array.Empty<Picture>().ToLookup(_ => 0);
    public ILookup<int, ProductAttributeCombination> Combinations { get; set; } = Array.Empty<ProductAttributeCombination>().ToLookup(c => c.ProductId);
    public ILookup<int, int> CombinationPictureIds { get; set; } = Array.Empty<int>().ToLookup(id => id);
    public Dictionary<int, string> Slugs { get; set; } = [];

    /// <summary>Products on the page with tier prices or their own discounts; they always use nopCommerce's price calculation.</summary>
    public HashSet<int> SpecialPrices { get; set; } = [];

    /// <summary>
    /// The store uses nopCommerce's own price calculation and has nothing that changes prices across many products
    /// (category or manufacturer discounts, price lists), so plain products can use their price as it is.
    /// </summary>
    public bool FastPrices { get; init; }

    // Kept for the whole run.

    /// <summary>Picture URLs by picture ID; many products share pictures.</summary>
    public Dictionary<int, string> PictureUrls { get; } = [];

    /// <summary>Price multiplier for tax, by tax category and tax exemption.</summary>
    public Dictionary<(int TaxCategoryId, bool Exempt), decimal> TaxFactors { get; } = [];

    /// <summary>Run-wide data for <see cref="Extensibility.IFeedItemExtension"/>s, keyed by a name each one picks.</summary>
    public Dictionary<string, object> Extensions { get; } = [];

    /// <summary>Extensions that failed in this run; they're skipped for the rest of it.</summary>
    internal HashSet<Extensibility.IFeedItemExtension> FailedExtensions { get; } = [];
}

/// <summary>Adds up the time spent in each build step.</summary>
public sealed class StepTimings
{
    private readonly Dictionary<string, long> _ticks = [];

    /// <summary>Adds the time since the clock was last restarted to a step, then restarts the clock.</summary>
    public void Add(string step, System.Diagnostics.Stopwatch clock)
    {
        _ticks[step] = _ticks.GetValueOrDefault(step) + clock.ElapsedTicks;
        clock.Restart();
    }

    public override string ToString()
        => string.Join(", ", _ticks.OrderByDescending(pair => pair.Value)
            .Select(pair => $"{pair.Key} {pair.Value * 1000 / System.Diagnostics.Stopwatch.Frequency / 1000.0:0.0} s"));
}

public sealed record BuiltItem(FeedItem Item, List<FeedIssue> Issues)
{
    public bool HeldBack => FeedChecks.IsHeldBack(Issues);
}

/// <summary>Turns nopCommerce products into Google items.</summary>
public class FeedBuilder(IRepository<Product> products,
    IRepository<ProductCategory> productCategories,
    IRepository<Category> categories,
    IRepository<Manufacturer> manufacturers,
    IRepository<MerchantProductSetting> productSettings,
    IRepository<ProductPicture> productPictures,
    IRepository<Picture> pictures,
    IRepository<ProductAttributeCombination> combinationRepository,
    IRepository<ProductAttributeCombinationPicture> combinationPictures,
    IRepository<UrlRecord> urlRecords,
    IRepository<TierPrice> tierPrices,
    IRepository<Nop.Core.Domain.Discounts.Discount> discountRepository,
    IRepository<Nop.Core.Domain.Discounts.DiscountProductMapping> discountProducts,
#if NOP_500
    IRepository<Nop.Core.Domain.PriceLists.PriceList> priceLists,
#endif
    MerchantFeedService feedService,
    IStoreService storeService,
    ILanguageService languageService,
    ICurrencyService currencyService,
    ICustomerService customerService,
    IStoreMappingService storeMappingService,
    IAclService aclService,
    ILocalizationService localization,
    IHtmlFormatter htmlFormatter,
    INopUrlHelper urlHelper,
    IPictureService pictureService,
    IPriceCalculationService priceCalculation,
    ITaxService taxService,
    IProductService productService,
    IProductAttributeService attributeService,
    IProductAttributeParser attributeParser,
    ICategoryService categoryService,
    IManufacturerService manufacturerService,
    IMeasureService measureService,
    MeasureSettings measureSettings,
    CurrencySettings currencySettings,
    Nop.Services.Configuration.ISettingService settingService,
    IEnumerable<Extensibility.IFeedItemExtension> extensions,
    Nop.Services.Logging.ILogger logger)
{
    private readonly Extensibility.IFeedItemExtension[] _extensions = extensions.OrderBy(e => e.Order).ToArray();

    public async Task<FeedContext> PrepareAsync(MerchantFeed feed)
    {
        var store = await storeService.GetStoreByIdAsync(feed.StoreId) ?? (await storeService.GetAllStoresAsync()).First();
        var language = await languageService.GetLanguageByIdAsync(feed.LanguageId)
            ?? (await languageService.GetAllLanguagesAsync(storeId: store.Id)).FirstOrDefault()
            ?? (await languageService.GetAllLanguagesAsync()).First();
        var currency = await currencyService.GetCurrencyByIdAsync(feed.CurrencyId)
            ?? await currencyService.GetCurrencyByIdAsync(currencySettings.PrimaryStoreCurrencyId);
        var settings = await settingService.LoadSettingAsync<MerchantCenterSettings>();
        var customer = await customerService.GetOrCreateSearchEngineUserAsync();

        var allCategories = (await categories.GetAllAsync(query => query.Where(c => c.Published && !c.Deleted), getCacheKey: null))
            .ToDictionary(c => c.Id);
        var categoryNames = new Dictionary<int, string>();
        foreach (var category in allCategories.Values)
            categoryNames[category.Id] = await localization.GetLocalizedAsync(category, c => c.Name, language.Id);
        var manufacturerNames = new Dictionary<int, string>();
        foreach (var manufacturer in await manufacturers.GetAllAsync(query => query.Where(m => m.Published && !m.Deleted), getCacheKey: null))
            manufacturerNames[manufacturer.Id] = await localization.GetLocalizedAsync(manufacturer, m => m.Name, language.Id);

        var weight = await measureService.GetMeasureWeightByIdAsync(measureSettings.BaseWeightId);
        var weightUnit = weight?.SystemKeyword?.ToLowerInvariant() switch
        {
            "ounce" or "oz" => "oz",
            "lb" or "lbs" or "pound" => "lb",
            "grams" or "gram" or "g" => "g",
            "kg" or "kilogram" or "kilograms" => "kg",
            _ => null
        };

        var storeUrl = store.Url ?? "";
        if (!storeUrl.EndsWith('/'))
            storeUrl += "/";

        var context = new FeedContext
        {
            Feed = feed,
            Store = store,
            StoreUrl = storeUrl,
            Language = language,
            Currency = currency,
            Customer = customer,
            Settings = settings,
            Categories = allCategories,
            CategoryNames = categoryNames,
            Mappings = await feedService.GetCategoryMappingsAsync(),
            ManufacturerNames = manufacturerNames,
            ProductSettings = await feedService.GetProductSettingsAsync(),
            CategoryFilter = ExpandCategories(ParseIds(feed.CategoryIds), allCategories),
            WeightUnit = weightUnit,
            LocalizedSlugs = (await languageService.GetAllLanguagesAsync()).Count >= 2,
            FastPrices = await CanUseFastPricesAsync(),
            ColorNames = Names(settings.ColorAttributeNames),
            SizeNames = Names(settings.SizeAttributeNames)
        };
        foreach (var extension in _extensions)
            await RunExtensionAsync(extension, context, () => extension.PrepareAsync(context));
        return context;
    }

    /// <summary>Runs one extension step. A failure is logged once and turns that extension off for the run.</summary>
    private async Task RunExtensionAsync(Extensibility.IFeedItemExtension extension, FeedContext context, Func<Task> step)
    {
        if (context.FailedExtensions.Contains(extension))
            return;
        try
        {
            await step();
        }
        catch (Exception exception)
        {
            context.FailedExtensions.Add(extension);
            await logger.ErrorAsync($"Merchant Center: the extension {extension.GetType().FullName} failed and was skipped for feed \"{context.Feed.Name}\". {exception.Message}", exception);
        }
    }

    private async Task ExtendAsync(Product product, FeedItem item, FeedContext context)
    {
        foreach (var extension in _extensions)
            await RunExtensionAsync(extension, context, () => extension.ApplyAsync(product, item, context));
    }

    private async Task<bool> CanUseFastPricesAsync()
    {
        // Another plugin (B2B pricing, for example) may replace the price calculation; then always use it.
        if (priceCalculation.GetType() != typeof(PriceCalculationService))
            return false;
        var catalogDiscounts = await discountRepository.Table.AnyAsync(discount =>
            discount.DiscountTypeId == (int)Nop.Core.Domain.Discounts.DiscountType.AssignedToCategories
            || discount.DiscountTypeId == (int)Nop.Core.Domain.Discounts.DiscountType.AssignedToManufacturers);
        if (catalogDiscounts)
            return false;
#if NOP_500
        if (await priceLists.Table.AnyAsync(list => list.Active))
            return false;
#endif
        return true;
    }

    public static int[] ParseIds(string csv)
        => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();

    private static HashSet<string> Names(string csv)
        => new((csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>The selected categories and all their subcategories. Null when nothing is selected.</summary>
    private static HashSet<int> ExpandCategories(int[] selected, Dictionary<int, Category> all)
    {
        if (selected.Length == 0)
            return null;
        var result = new HashSet<int>(selected);
        bool added;
        do
        {
            added = false;
            foreach (var category in all.Values)
                if (!result.Contains(category.Id) && result.Contains(category.ParentCategoryId))
                    added = result.Add(category.Id) || added;
        } while (added);
        return result;
    }

    /// <summary>Products that can go into the feed: published, visible on their own, available now, not excluded.</summary>
    public async Task<IQueryable<Product>> QueryAsync(FeedContext context)
    {
        var now = DateTime.UtcNow;
        var query = products.Table.Where(p => p.Published && !p.Deleted && p.VisibleIndividually
            && p.ProductTypeId == (int)ProductType.SimpleProduct
            && (!p.AvailableStartDateTimeUtc.HasValue || p.AvailableStartDateTimeUtc <= now)
            && (!p.AvailableEndDateTimeUtc.HasValue || p.AvailableEndDateTimeUtc >= now));
        query = await storeMappingService.ApplyStoreMapping(query, context.Store.Id);
        query = await aclService.ApplyAcl(query, context.Customer);

        var excluded = productSettings.Table.Where(setting => setting.Excluded).Select(setting => setting.ProductId);
        query = query.Where(p => !excluded.Contains(p.Id));
        if (context.Feed.MinPrice is > 0)
        {
            var min = context.Feed.MinPrice.Value;
            query = query.Where(p => p.Price >= min);
        }
        if (context.CategoryFilter != null)
        {
            var ids = context.CategoryFilter.ToList();
            var inCategories = productCategories.Table.Where(pc => ids.Contains(pc.CategoryId)).Select(pc => pc.ProductId);
            query = query.Where(p => inCategories.Contains(p.Id));
        }
        return query.OrderBy(p => p.Id);
    }

    /// <summary>Loads category and manufacturer links for a page of products in two queries.</summary>
    public async Task LoadPageAsync(FeedContext context, IList<Product> page)
    {
        var ids = page.Select(p => p.Id).ToArray();
        context.ProductCategoryIds = await categoryService.GetProductCategoryIdsAsync(ids);
        context.ProductManufacturerIds = await manufacturerService.GetProductManufacturerIdsAsync(ids);

        var max = Math.Clamp(context.Settings.MaxAdditionalImages, 0, FeedChecks.MaxAdditionalImages) + 1;
        var pictureLinks = await productPictures.Table.Where(pp => ids.Contains(pp.ProductId))
            .OrderBy(pp => pp.ProductId).ThenBy(pp => pp.DisplayOrder).ThenBy(pp => pp.Id)
            .Select(pp => new { pp.ProductId, pp.PictureId }).ToListAsync();
        var pictureIds = pictureLinks.Select(link => link.PictureId).Distinct().ToList();
        var pictureById = (await pictures.Table.Where(picture => pictureIds.Contains(picture.Id)).ToListAsync())
            .ToDictionary(picture => picture.Id);
        context.Pictures = pictureLinks.Where(link => pictureById.ContainsKey(link.PictureId))
            .GroupBy(link => link.ProductId)
            .SelectMany(group => group.Take(max).Select(link => (link.ProductId, Picture: pictureById[link.PictureId])))
            .ToLookup(pair => pair.ProductId, pair => pair.Picture);

        var needCombinations = context.Settings.SplitCombinations
            || page.Any(p => p.ManageInventoryMethod == ManageInventoryMethod.ManageStockByAttributes);
        context.Combinations = needCombinations
            ? (await combinationRepository.Table.Where(c => ids.Contains(c.ProductId)).OrderBy(c => c.Id).ToListAsync()).ToLookup(c => c.ProductId)
            : Array.Empty<ProductAttributeCombination>().ToLookup(c => c.ProductId);
        var combinationIds = context.Combinations.SelectMany(group => group).Select(c => c.Id).ToList();
        context.CombinationPictureIds = combinationIds.Count == 0
            ? Array.Empty<int>().ToLookup(id => id)
            : (await combinationPictures.Table.Where(cp => combinationIds.Contains(cp.ProductAttributeCombinationId))
                .OrderBy(cp => cp.Id).Select(cp => new { cp.ProductAttributeCombinationId, cp.PictureId }).ToListAsync())
                .ToLookup(cp => cp.ProductAttributeCombinationId, cp => cp.PictureId);

        var withTierPrices = await tierPrices.Table.Where(tp => ids.Contains(tp.ProductId)).Select(tp => tp.ProductId).Distinct().ToListAsync();
        var withDiscounts = await discountProducts.Table.Where(dp => ids.Contains(dp.EntityId)).Select(dp => dp.EntityId).Distinct().ToListAsync();
        context.SpecialPrices = [.. withTierPrices, .. withDiscounts];

        // Active URL names, picked the way IUrlRecordService.GetSeNameAsync does: the feed language's when two or
        // more languages are published, otherwise (or when it has none) the standard one.
        var languageId = context.Language.Id;
        var slugRows = await urlRecords.Table
            .Where(u => u.EntityName == nameof(Product) && u.IsActive && ids.Contains(u.EntityId) && (u.LanguageId == 0 || u.LanguageId == languageId))
            .Select(u => new { u.EntityId, u.LanguageId, u.Slug, u.Id }).ToListAsync();
        context.Slugs = slugRows.GroupBy(u => u.EntityId).ToDictionary(group => group.Key, group =>
            (context.LocalizedSlugs ? group.Where(u => u.LanguageId == languageId && languageId != 0).OrderByDescending(u => u.Id).FirstOrDefault()?.Slug : null)
            ?? group.Where(u => u.LanguageId == 0).OrderByDescending(u => u.Id).FirstOrDefault()?.Slug);

        if (_extensions.Length > 0)
        {
            var readOnlyPage = page as IReadOnlyList<Product> ?? page.ToList();
            foreach (var extension in _extensions)
                await RunExtensionAsync(extension, context, () => extension.PreparePageAsync(context, readOnlyPage));
        }
    }

    /// <summary>One item for the product, or one per attribute combination. Checks are applied.</summary>
    public async Task<List<BuiltItem>> BuildAsync(Product product, FeedContext context)
    {
        var settings = context.Settings;
        var languageId = context.Language.Id;
        var source = (OfferIdSource)settings.OfferIdSource;
        var productSetting = context.ProductSettings.GetValueOrDefault(product.Id);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var title = await localization.GetLocalizedAsync(product, p => p.Name, languageId);
        var description = PlainText(await localization.GetLocalizedAsync(product, p => p.FullDescription, languageId));
        if (description.Length == 0 && settings.UseShortDescriptionFallback)
            description = PlainText(await localization.GetLocalizedAsync(product, p => p.ShortDescription, languageId));

        context.Timings.Add("text", clock);
        var link = await ProductUrlAsync(product, context);
        context.Timings.Add("url", clock);
        var images = await ImagesAsync(product, context);
        context.Timings.Add("pictures", clock);

        var categoryIds = context.ProductCategoryIds.TryGetValue(product.Id, out var linked) ? linked : [];
        var visibleCategory = categoryIds.FirstOrDefault(context.Categories.ContainsKey);
        var googleCategory = categoryIds.Select(id => GoogleCategory(id, context)).FirstOrDefault(id => id != null);
        var productType = visibleCategory == 0 ? null : CategoryPath(visibleCategory, context);

        var manufacturerIds = context.ProductManufacturerIds.TryGetValue(product.Id, out var makers) ? makers : [];
        var brand = manufacturerIds.Select(id => context.ManufacturerNames.GetValueOrDefault(id)).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        if (string.IsNullOrWhiteSpace(brand) && !string.IsNullOrWhiteSpace(settings.DefaultBrand))
            brand = settings.DefaultBrand;

        var productOfferId = OfferIds.ForProduct(source, product.Id, product.Sku);

        FeedItem Base()
        {
            var item = new FeedItem
            {
                ProductId = product.Id,
                Title = title,
                Description = description,
                Link = link,
                Currency = context.Currency.CurrencyCode,
                Brand = brand,
                Condition = productSetting?.Condition ?? "new",
                GoogleProductCategory = googleCategory,
                ProductType = productType,
                Gender = Optional(settings.DefaultGender),
                AgeGroup = Optional(settings.DefaultAgeGroup)
            };
            if (settings.IncludeShippingWeight && product.IsShipEnabled && product.Weight > 0 && context.WeightUnit != null)
            {
                item.ShippingWeight = product.Weight;
                item.ShippingWeightUnit = context.WeightUnit;
            }
            return item;
        }

        var built = new List<BuiltItem>();
        var combinations = settings.SplitCombinations ? context.Combinations[product.Id].ToList() : [];
        context.Timings.Add("combinations", clock);
        if (combinations.Count == 0)
        {
            var item = Base();
            item.Id = productOfferId ?? "";
            item.Gtin = product.Gtin;
            item.Mpn = product.ManufacturerPartNumber;
            item.ImageLink = images.FirstOrDefault();
            item.AdditionalImageLinks.AddRange(images.Skip(1));
            await ApplyPricesAsync(item, product, null, 0, context);
            context.Timings.Add("prices", clock);
            item.Availability = await AvailabilityAsync(product, null, context);
            context.Timings.Add("stock", clock);
            item.AvailabilityDateUtc = product.PreOrderAvailabilityStartDateTimeUtc;
            await ExtendAsync(product, item, context);
            built.Add(new BuiltItem(item, FeedChecks.Apply(item, settings.NoIdentifiersMeansCustom)));
            return built;
        }

        foreach (var combination in combinations)
        {
            var item = Base();
            item.CombinationId = combination.Id;
            item.ItemGroupId = productOfferId;
            item.Id = OfferIds.ForCombination(source, productOfferId, combination.Id, combination.Sku) ?? "";
            item.Gtin = combination.Gtin;
            item.Mpn = combination.ManufacturerPartNumber;

            var values = await attributeParser.ParseProductAttributeValuesAsync(combination.AttributesXml);
            var valueNames = new List<string>();
            decimal additionalCharge = 0;
            foreach (var value in values)
            {
                var valueName = await localization.GetLocalizedAsync(value, v => v.Name, languageId);
                valueNames.Add(valueName);
                var attributeName = await AttributeNameAsync(value.ProductAttributeMappingId, context);
                if (context.ColorNames.Contains(attributeName))
                    item.Color = valueName;
                else if (context.SizeNames.Contains(attributeName))
                    item.Size = valueName;
                additionalCharge += await priceCalculation.GetProductAttributeValuePriceAdjustmentAsync(product, value, context.Customer, context.Store);
            }
            if (valueNames.Count > 0)
                item.Title = $"{title} - {string.Join(", ", valueNames)}";

            var combinationPictureId = context.CombinationPictureIds[combination.Id].FirstOrDefault();
            var combinationImage = combinationPictureId == 0 ? null : await PictureUrlAsync(combinationPictureId, null, context);
            item.ImageLink = string.IsNullOrEmpty(combinationImage) ? images.FirstOrDefault() : combinationImage;
            item.AdditionalImageLinks.AddRange(images.Where(image => image != item.ImageLink).Take(settings.MaxAdditionalImages));

            await ApplyPricesAsync(item, product, combination.OverriddenPrice, additionalCharge, context);
            context.Timings.Add("prices", clock);
            item.Availability = await AvailabilityAsync(product, combination, context);
            context.Timings.Add("stock", clock);
            item.AvailabilityDateUtc = product.PreOrderAvailabilityStartDateTimeUtc;
            await ExtendAsync(product, item, context);
            built.Add(new BuiltItem(item, FeedChecks.Apply(item, settings.NoIdentifiersMeansCustom)));
        }
        return built;
    }

    private static readonly System.Text.RegularExpressions.Regex BlockTags = new(
        @"<\s*(br|/?p|/?li|/?div|/?h[1-6]|/?tr|/?td|/?th|/?ul|/?ol|/?table|/?section|/?blockquote)\b[^>]*>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string Optional(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string PlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        // Block tags end a sentence or list item; without a space, "fit.</li><li>Slim" becomes "fit.Slim".
        html = BlockTags.Replace(html, " ");
        return Text.Clean(htmlFormatter.StripTags(htmlFormatter.ConvertHtmlToPlainText(html, decode: true)));
    }

    private async Task<string> ProductUrlAsync(Product product, FeedContext context)
    {
        // What RouteGenericUrlAsync(product, languageId) does, with the URL name loaded for the whole page.
        if (!context.Slugs.TryGetValue(product.Id, out var seName) || string.IsNullOrEmpty(seName))
            return "";
        var path = await urlHelper.RouteGenericUrlAsync<Product>(new { SeName = seName });
        if (string.IsNullOrEmpty(path))
            return "";
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return path;
        return context.StoreUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private async Task<List<string>> ImagesAsync(Product product, FeedContext context)
    {
        var urls = new List<string>();
        foreach (var picture in context.Pictures[product.Id])
        {
            var url = await PictureUrlAsync(picture.Id, picture, context);
            if (!string.IsNullOrEmpty(url))
                urls.Add(url);
        }
        return urls;
    }

    private async Task<string> PictureUrlAsync(int pictureId, Picture picture, FeedContext context)
    {
        if (context.PictureUrls.TryGetValue(pictureId, out var cached))
            return cached;
        var url = picture != null
            ? (await pictureService.GetPictureUrlAsync(picture, context.Settings.PictureSize, false, context.StoreUrl)).Url
            : await pictureService.GetPictureUrlAsync(pictureId, context.Settings.PictureSize, false, context.StoreUrl);
        if (context.PictureUrls.Count < 200000)
            context.PictureUrls[pictureId] = url;
        return url;
    }

    /// <summary>The mapped Google category of a store category, or of its nearest mapped parent.</summary>
    private static int? GoogleCategory(int categoryId, FeedContext context)
    {
        for (var depth = 0; categoryId != 0 && depth < 20; depth++)
        {
            if (context.Mappings.TryGetValue(categoryId, out var google))
                return google;
            if (!context.Categories.TryGetValue(categoryId, out var category))
                return null;
            categoryId = category.ParentCategoryId;
        }
        return null;
    }

    public static string CategoryPath(int categoryId, FeedContext context)
    {
        var names = new List<string>();
        for (var depth = 0; categoryId != 0 && depth < 20 && context.Categories.TryGetValue(categoryId, out var category); depth++)
        {
            names.Add(context.CategoryNames.GetValueOrDefault(category.Id, category.Name));
            categoryId = category.ParentCategoryId;
        }
        names.Reverse();
        return names.Count == 0 ? null : string.Join(" > ", names);
    }

    private async Task<string> AttributeNameAsync(int mappingId, FeedContext context)
    {
        if (context.AttributeNames.TryGetValue(mappingId, out var name))
            return name;
        var mapping = await attributeService.GetProductAttributeMappingByIdAsync(mappingId);
        var attribute = mapping == null ? null : await attributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
        name = attribute?.Name ?? "";
        context.AttributeNames[mappingId] = name;
        return name;
    }

    /// <summary>
    /// Price and sale price as a guest sees them. "Old price" counts as the regular price, as on the product page, and
    /// discounts with dates give the sale dates.
    /// </summary>
    private async Task ApplyPricesAsync(FeedItem item, Product product, decimal? overriddenPrice, decimal additionalCharge, FeedContext context)
    {
        if (product.CallForPrice)
        {
            item.Price = 0;
            return;
        }

        decimal regular, final;
        List<Nop.Core.Domain.Discounts.Discount> discounts = [];
        if (product.CustomerEntersPrice)
            regular = final = product.MinimumCustomerEnteredPrice;
        else
        {
            if (context.FastPrices && !product.IsRental
                && !context.SpecialPrices.Contains(product.Id))
            {
                // What PriceCalculationService returns for a product without tier prices or discounts.
                regular = final = Math.Max(0, (overriddenPrice ?? product.Price) + additionalCharge);
            }
            else
            {
                var priceClock = System.Diagnostics.Stopwatch.StartNew();
                (regular, final, _, discounts) = await priceCalculation.GetFinalPriceAsync(product, context.Customer, context.Store,
                    overriddenPrice, additionalCharge, true, 1, null, null);
                context.Timings.Add("price calculation", priceClock);
            }
            if (overriddenPrice == null && product.OldPrice > regular)
                regular = product.OldPrice;
        }

        item.Price = await ConvertAsync(product, regular, context);
        if (final < regular)
        {
            item.SalePrice = await ConvertAsync(product, final, context);
            var dated = discounts.Where(discount => discount.StartDateUtc.HasValue || discount.EndDateUtc.HasValue).ToList();
            if (dated.Count > 0 && final < regular && discounts.Count == dated.Count)
            {
                item.SaleStartUtc = dated.Max(discount => discount.StartDateUtc);
                item.SaleEndUtc = dated.Min(discount => discount.EndDateUtc);
            }
        }
    }

    private async Task<decimal> ConvertAsync(Product product, decimal price, FeedContext context)
    {
        if (price != 0)
        {
            // Tax is a percentage per tax category, so it's worked out once per category for the run.
            var key = (product.TaxCategoryId, product.IsTaxExempt);
            if (!context.TaxFactors.TryGetValue(key, out var factor))
            {
                const decimal sample = 1000m;
                var (taxed, _) = await taxService.GetProductPriceAsync(product, sample, context.Feed.PricesIncludeTax, context.Customer);
                factor = taxed / sample;
                context.TaxFactors[key] = factor;
            }
            price *= factor;
        }
        price = await currencyService.ConvertFromPrimaryStoreCurrencyAsync(price, context.Currency);
        return await priceCalculation.RoundPriceAsync(price, context.Currency);
    }

    private async Task<ItemAvailability> AvailabilityAsync(Product product, ProductAttributeCombination combination, FeedContext context)
    {
        if (product.AvailableForPreOrder
            && (!product.PreOrderAvailabilityStartDateTimeUtc.HasValue || product.PreOrderAvailabilityStartDateTimeUtc > DateTime.UtcNow))
            return ItemAvailability.Preorder;

        switch (product.ManageInventoryMethod)
        {
            case ManageInventoryMethod.ManageStock:
                if (product.BackorderMode != BackorderMode.NoBackorders)
                    return ItemAvailability.InStock;
                return await productService.GetTotalStockQuantityAsync(product) > 0 ? ItemAvailability.InStock : ItemAvailability.OutOfStock;

            case ManageInventoryMethod.ManageStockByAttributes:
                if (combination != null)
                    return combination.StockQuantity > 0 || combination.AllowOutOfStockOrders ? ItemAvailability.InStock : ItemAvailability.OutOfStock;
                var all = context.Combinations[product.Id].ToList();
                return all.Count == 0 || all.Any(c => c.StockQuantity > 0 || c.AllowOutOfStockOrders)
                    ? ItemAvailability.InStock : ItemAvailability.OutOfStock;

            default:
                return ItemAvailability.InStock;
        }
    }
}
