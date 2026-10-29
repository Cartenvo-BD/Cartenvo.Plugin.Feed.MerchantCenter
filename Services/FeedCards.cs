using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Models;
using Nop.Core;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Stores;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Feed summaries for the admin pages.</summary>
public class FeedCards(FeedGenerator generator,
    MerchantSyncStore syncStore,
    ICountryService countryService,
    ILanguageService languageService,
    ICurrencyService currencyService,
    IStoreService storeService,
    IStoreContext storeContext,
    ILocalizationService localization)
{
    public async Task<FeedCardModel> CardAsync(MerchantFeed feed)
    {
        var info = new FileInfo(generator.FeedFilePath(feed));
        var countries = await countryService.GetAllCountriesAsync(showHidden: true);
        var country = countries.FirstOrDefault(c => string.Equals(c.TwoLetterIsoCode, feed.CountryCode, StringComparison.OrdinalIgnoreCase));
        var card = new FeedCardModel
        {
            Feed = feed,
            Url = await FeedUrlAsync(feed),
            CountryName = country == null ? feed.CountryCode : await localization.GetLocalizedAsync(country, c => c.Name),
            LanguageName = (await languageService.GetLanguageByIdAsync(feed.LanguageId))?.Name,
            CurrencyCode = (await currencyService.GetCurrencyByIdAsync(feed.CurrencyId))?.CurrencyCode,
            StoreName = (await storeService.GetStoreByIdAsync(feed.StoreId))?.Name,
            FileExists = info.Exists,
            FileSize = info.Exists ? info.Length : 0,
            Issues = MerchantFeedService.IssueSummary(feed)
        };
        if (feed.IsApi)
        {
            card.Sync = await syncStore.CountsAsync(feed.Id);
            card.Queued = await syncStore.CountQueuedAsync();
        }
        return card;
    }

    public async Task<string> FeedUrlAsync(MerchantFeed feed)
    {
        var store = await storeService.GetStoreByIdAsync(feed.StoreId) ?? await storeContext.GetCurrentStoreAsync();
        var url = store.Url ?? "";
        if (!url.EndsWith('/'))
            url += "/";
        return $"{url}merchant-center/feed/{feed.Id}/{feed.Token}.xml";
    }
}
