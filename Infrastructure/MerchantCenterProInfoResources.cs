namespace Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;

/// <summary>English texts for the page about the paid Pro add-on, shown while Pro isn't installed. Keys get the plugin's resource prefix.</summary>
public static class MerchantCenterProInfoResources
{
    public static Dictionary<string, string> Texts() => new()
    {
        ["Nav.ProInfo"] = "Pro",
        ["Nav.ProInfo.Tag"] = "Add-on",

        ["ProInfo.Title"] = "Merchant Center Pro",
        ["ProInfo.Intro"] = "A paid add-on for this plugin, for stores that run Google Shopping ads. It fills Google Ads custom labels, writes better titles and shows how each product does in Google.",
        ["ProInfo.Free"] = "Everything you use now stays free, with no limits. Pro only adds to it.",
        ["ProInfo.Buy"] = "See Merchant Center Pro",
        ["ProInfo.Try"] = "Install it and its pages show your own products before and after, without a licence. It also runs free on localhost and test sites.",

        ["ProInfo.Labels"] = "Custom labels for Google Ads",
        ["ProInfo.Labels.Text"] = "Fills up to five custom labels automatically, so campaigns can bid by profit margin, price band, best sellers, stock level, new arrivals, top category or any specification attribute.",
        ["ProInfo.Labels.Example"] = "margin-high · price-100-250 · best-seller",
        ["ProInfo.Titles"] = "Title rules",
        ["ProInfo.Titles.Text"] = "Builds titles from a template, with one per category if you like. Empty values and their separators drop out.",
        ["ProInfo.Titles.Example"] = "{brand} {name} {spec:Material} - {variant}",
        ["ProInfo.Performance"] = "Performance in Google",
        ["ProInfo.Performance.Text"] = "Impressions, clicks, click rate and conversions per product for the last 30 days, read daily. Lists of the most clicked products and of products seen but never clicked.",
        ["ProInfo.Performance.Example"] = "1,240 impressions · 38 clicks · 3.1%",
        ["ProInfo.Prices"] = "Prices against other shops",
        ["ProInfo.Prices.Text"] = "Google's price benchmark for each product, and labels for products below, at or above other shops' prices, so ads can push the ones that win on price.",
        ["ProInfo.Prices.Example"] = "price-below · price-above",

        ["ProInfo.Compare"] = "Free and Pro",
        ["ProInfo.Compare.Feature"] = "Feature",
        ["ProInfo.Compare.Free"] = "Free",
        ["ProInfo.Compare.Pro"] = "Pro",
        ["ProInfo.Compare.Feed"] = "Feed file and automatic sync through the Merchant API",
        ["ProInfo.Compare.Checks"] = "Checks before sending, Google categories, variants",
        ["ProInfo.Compare.Status"] = "Products in Google: approvals and Google's issues",
        ["ProInfo.Compare.Labels"] = "Custom labels filled automatically",
        ["ProInfo.Compare.Titles"] = "Title templates",
        ["ProInfo.Compare.Performance"] = "Clicks, impressions and price benchmark",

        ["ProInfo.Later"] = "Coming to Pro later: promotions, local inventory, shipping and tax overrides, and feeds for Microsoft, Meta and Pinterest.",

        ["Overview.ProPromo.Title"] = "Run Shopping ads? Try Merchant Center Pro",
        ["Overview.ProPromo.Text"] = "Custom labels by margin and best sellers, title templates and clicks per product."
    };
}
