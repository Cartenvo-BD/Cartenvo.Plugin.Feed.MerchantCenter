# Merchant Center Feed for Google (nopCommerce)

**Free Google Merchant Center product feed for nopCommerce 4.80, 4.90 and 5.00, by [Cartenvo](https://cartenvo.com).**

Put your products on Google Shopping, in free listings and in Shopping ads. The plugin builds a product feed, keeps it
up to date and tells you, product by product, what Google would reject and how to fix it. Connect it to Google's
Merchant API and changes reach Google within minutes.

## Features

- **One-click setup.** The first feed is created with your store, country, language and currency filled in, and is
  built straight away. A guide shows how to add its URL in Merchant Center.
- **Checks before Google sees it.** Products Google would reject are held back and listed with the reason and how to
  fix it: no picture, no price, no description, no product page, duplicate IDs. Warnings cover missing brands, GTINs
  and part numbers, invalid GTIN check digits (the GTIN is left out so the product stays listed), names in capital
  letters, long names and descriptions, and pages that aren't HTTPS.
- **Category mapping with suggestions.** Each store category gets a suggested Google category, from Google's own list.
  Accept them one by one or all at once, or search Google's categories. Subcategories inherit their parent's mapping.
- **Variants.** Each attribute combination can be sent as its own product, with its own ID, price, stock, GTIN and
  picture, grouped under the product. Colour and size come from your attribute names.
- **Prices as shoppers see them.** "Old price" and discounts become Google's sale price, with the discount's dates.
  Prices are converted to the feed's currency and include tax or not, as Google expects for the country.
- **Preview any product.** See exactly what Google receives, a mock of the listing, every attribute and the XML.
- **Several feeds.** One per country, language, currency or store, each filtered by category, stock or price.
- **Automatic updates.** A scheduled task rebuilds feeds every 6 hours by default. Merchant Center fetches them from
  a private URL.
- **Automatic sync through Google's Merchant API.** Optional. New products, prices, stock and removals reach Google
  within 5 minutes, instead of once a day. Only products that changed are sent, and every product is compared with
  Google once a day.
- **Products in Google.** See which products Google approved, limited or refused, with Google's own explanation of
  each problem and a link to how to fix it. Works with feed files too: only the connection is needed. A panel on the
  product edit page shows that product's status.
- **Product IDs that match your old plugin.** SKU, product ID, or SKU with the product ID as a fallback, so moving
  from another Google Shopping plugin keeps your products' history.

## How it works

1. Install the plugin and open **Promotions → Google Merchant Center**.
2. Click **Create and build my feed**.
3. Copy the feed URL into Merchant Center (**Settings → Data sources → Add product source → Add products from a
   file → Enter a link to your file**).
4. Map your categories, fix what's held back, and build again. Google fetches the feed every day.

### Automatic sync (optional)

Open **Google connection** and follow the three steps (about 10 minutes):

1. In Google Cloud, enable the Merchant API and create a service account with a JSON key. Drop the key file on the
   page and enter your Merchant Center ID. **Save and test** checks the key with Google.
2. In Merchant Center, add the service account's email under **People and access** as Admin, then click **Register**
   (Google needs this once for each Google Cloud project).
3. Click **Connect**. The plugin creates its own data source, "nopCommerce (Cartenvo)", in Merchant Center.

Then turn on **Sync automatically** for a feed. The first sync sends every product. After that:

- Saving a product, its stock, price, pictures, variants, categories, discounts or SEO name queues it, and a scheduled
  task ("Merchant Center sync (Cartenvo)", every 5 minutes) sends it.
- Products whose data hasn't changed aren't sent, so Google's limits aren't reached. Calls run 5 at a time and back
  off when Google asks.
- Deleted, unpublished, filtered-out and held-back products are removed from Google.
- Once a day every product is compared with what was sent, which also refreshes products before Google's 30-day
  expiry.
- **Products Google refused** lists each refusal with Google's reason. A fixed product is sent again with the next
  sync.

### Products in Google

With the connection set up (steps 1 and 2 are enough; automatic sync isn't needed), the **Products in Google** tab
reads every product from Merchant Center every 12 hours, or when you click **Refresh now**:

- how many products are approved, limited (refused in a country or shown less), disapproved and pending;
- Google's issues grouped by kind, most serious first, with how many products each affects and Google's help link;
- every product with its status and issues, searchable and filterable by status or issue.

Products are matched to your catalogue by item ID, whichever data source they came from. The product edit page gets a
**Google Merchant Center** panel with that product's status per country. Google reviews changes within a few hours.

Switching a feed back to a feed file removes its products from the plugin's data source within 5 minutes. If you
added a feed's URL in Merchant Center before switching it to automatic sync, delete that data source in Merchant
Center after the first sync, or products are listed twice.

The plugin calls Google's REST API directly, signing in with the service account key itself, so it doesn't depend on
the Google libraries nopCommerce ships (which differ between versions).

The feed URL contains a secret token. Only Merchant Center, or someone you give the link to, can read it. **New URL**
replaces the token. Feeds are stored in `App_Data/CartenvoMerchantCenter`, outside `wwwroot`.

## Privacy

With feed files, the plugin sends nothing anywhere: Google fetches the feed from your store. The only outgoing request
is a one-time download of Google's product category list from `google.com`, cached in `App_Data`, for category
mapping. If the server can't reach Google, you can still type category IDs.

With automatic sync, the plugin sends product data to Google (`oauth2.googleapis.com` and
`merchantapi.googleapis.com`) and nothing else. The service account key is stored encrypted with the store's
encryption key. Uninstalling the plugin deletes the key; products already sent stay in Merchant Center until Google
expires them after 30 days, or until you delete the "nopCommerce (Cartenvo)" data source there.

## Large catalogs

Feeds are built in pages of 500 products, with pictures, variants, URL names, tier prices and discounts loaded per page.
Products without tier prices or discounts skip nopCommerce's per-product price calculation when the store has no
category or manufacturer discounts (and, on 5.00, no price lists) and uses nopCommerce's own price calculation.
Otherwise nopCommerce's price calculation is used for every product.

Tested on a 100,043-product catalog with PostgreSQL: the whole feed was built in about 3 minutes. Long builds write a
progress line to **System → Log** about once a minute. At most 20,000 problem rows are kept for each feed; the counts
are always complete.

## Supported versions

| nopCommerce | .NET | Package folder |
|---|---|---|
| 5.00 | 10 | `5.00/Feed.CartenvoMerchantCenter` |
| 4.90 | 9 | `4.90/Feed.CartenvoMerchantCenter` |
| 4.80 | 9 | `4.80/Feed.CartenvoMerchantCenter` |

One marketplace package holds all three builds. nopCommerce installs the one for its version.

## Building

Place this folder in `src/Plugins` of a nopCommerce 4.80, 4.90 or 5.00 source tree and build
`Cartenvo.Plugin.Feed.MerchantCenter.csproj`. The project reads the nopCommerce version from the tree's `Nop.Core.csproj`
(or pass `-p:NopVersion=4.80`). The feed logic that doesn't depend on nopCommerce lives in `MerchantCenter.Core` and is
tested by `Tests/MerchantCenter.Core.Tests`:

```bash
dotnet test Tests/MerchantCenter.Core.Tests
```

## Extending

Other plugins can add to this one without changing it. Register implementations in your plugin's `INopStartup`:

| Interface | What it does |
|---|---|
| `IFeedItemExtension` | Changes each product before it's checked and sent, in the feed file, the API sync and the preview. For example, set `FeedItem.CustomLabels` or rewrite the title. |
| `IAdminTabProvider` | Adds tabs to the plugin's admin pages. Your views can use `Views/_Header.cshtml` for the same frame. |

```csharp
public class SeasonLabel : IFeedItemExtension
{
    public Task ApplyAsync(Product product, FeedItem item, FeedContext context)
    {
        item.CustomLabels[0] = DateTime.UtcNow.Month is 11 or 12 ? "holiday" : null;
        return Task.CompletedTask;
    }
}
```

An extension that throws is logged and skipped for the rest of that run, so products still go out. The interfaces only
ever gain members with default implementations; `ExtensionApi.Version` goes up when they do. Declare the dependency in
your `plugin.json` with `"DependsOnSystemNames": [ "Feed.CartenvoMerchantCenter" ]`.

[Merchant Center Pro](https://cartenvo.com/plugins/merchant-center) is Cartenvo's paid add-on built this way: Google Ads
custom labels, title rules and more.

## Licence

Apache 2.0. Need help with Google Shopping, feeds or nopCommerce? [Cartenvo](https://cartenvo.com/services) builds and
maintains nopCommerce stores.
