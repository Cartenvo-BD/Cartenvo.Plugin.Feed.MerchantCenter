# Changelog

Versions are `<nopCommerce version>.<release>`, so release 1 is `4.80.1`, `4.90.1` and `5.00.1`.

## 2

Automatic sync through Google's Merchant API v1 (optional; feed files work as before).

- **Google connection** page: three guided steps with a check at each (key, access and registration, connect),
  explanations for every Google error, and the plugin's own API data source in Merchant Center.
- **Sync automatically** per feed. Product, variant, stock, price, picture, category, manufacturer, tier price,
  discount, SEO name and translation changes are queued and sent within 5 minutes. Changes that affect many products
  (a category or currency) queue a full comparison, at most once an hour.
- Only changed products are sent (content hash). Calls run in parallel and retry with back-off on rate limits and
  server errors. Products are removed from Google when they're deleted, unpublished, filtered out, held back or get a
  new ID, and when a feed is deleted or switched back to a file.
- A daily full comparison also refreshes every product before Google's 30-day expiry.
- **Products Google refused** list with Google's reasons; the overview shows each feed's sync state.
- Custom labels 0–4 in the feed file and the API sync.
- Hook points for other plugins: `IFeedItemExtension` changes products before they're sent, and `IAdminTabProvider`
  adds admin tabs (see README, Extending).
- The Google connection tab shows a dot: green when connected, amber when set-up isn't finished, "Optional" otherwise.
- The connection page explains every step in detail, including where to find the Merchant Center ID.
- Fixed: the "Google Merchant Center" item now appears in the admin menu under Promotions (release 1 never added it).
- Upgrading from release 1 adds the tables, the sync task and the new texts.
- Tested against a local stand-in for Google's API on 4.80 and 5.00 (SQL Server) and 4.90 (PostgreSQL, a full pass
  over 100,043 products in about 3 minutes).

## 1

First release.

- **Feed file** for Google Merchant Center (RSS 2.0 with the `g:` namespace), served at a private URL with a secret
  token and rebuilt by a scheduled task (every 6 hours by default).
- **One-click setup** with store, country, language, currency and tax filled in, a live build progress bar and a guide
  for adding the feed in Merchant Center.
- **Checks** that hold back products Google would reject (no ID, duplicate ID, no name, no description, no product
  page, no picture, no price) and warn about the rest (invalid GTIN, no brand, no GTIN or part number, long name or
  description, capital letters, http pages, unmapped category, out of stock).
- **Products to fix** page, filtered by problem, with how to fix each one, links to edit the product, and "leave out".
- **Category mapping** to Google's taxonomy, with suggestions, "accept all", search and inheritance from parents.
- **Variants** as separate items with `item_group_id`, colour, size, own price, stock, GTIN and picture.
- **Prices**: old price and discounts become `sale_price` with `sale_price_effective_date`; currency conversion;
  prices with or without tax.
- **Preview** of any product: listing mock, attributes, problems and XML. Condition (new, used, refurbished) and
  "leave out" per product.
- **Several feeds** per country, language, currency and store, filtered by category, stock and minimum price.
- **Settings** for product IDs, default brand, custom-made products, variants, pictures, clothing gender and age group,
  shipping weight and the rebuild schedule.
- Large catalogs: pictures, variants, URL names, tier prices and discounts are loaded per page of 500 products, and tax
  once per tax category. Tested on 100,043 products with PostgreSQL (about 3 minutes).
- Tested on nopCommerce 4.80 (SQL Server), 4.90 (PostgreSQL) and 5.00 (SQL Server).
