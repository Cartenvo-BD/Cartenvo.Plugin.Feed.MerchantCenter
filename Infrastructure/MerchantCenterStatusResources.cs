namespace Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;

/// <summary>English texts for product status read back from Google (release 3). Keys get the plugin's resource prefix.</summary>
public static class MerchantCenterStatusResources
{
    public static Dictionary<string, string> Texts() => new()
    {
        ["Nav.Google"] = "Products in Google",

        ["Status.Title"] = "Products in Google",
        ["Status.Intro"] = "What Google does with your products, read back from Merchant Center: which are shown, which are limited or refused, and Google's own explanation of how to fix each problem.",
        ["Status.Connect"] = "Connect to Google once to see which products Google shows and why it refuses others. It works with feed files too: automatic sync isn't needed.",
        ["Status.Refresh"] = "Refresh now",
        ["Status.Reading"] = "Reading your products from Merchant Center…",
        ["Status.Read"] = "Read {0} products from Merchant Center; {1} matched products in this store.",
        ["Status.ReadOn"] = "Updated {0}",
        ["Status.Failed"] = "The last update from Google failed.",
        ["Status.AlreadyRunning"] = "Statuses are being read from Google already. Try again in a minute.",
        ["Status.NotConnected"] = "Not connected to Merchant Center. Set up the connection on the Google connection tab.",
        ["Status.Never"] = "Not read from Google yet",
        ["Status.Never.Text"] = "Click Refresh now, or wait for the hourly task. Google needs a few hours to review new products.",
        ["Status.Every"] = "Statuses are read from Google every {0} hours. Google reviews changes within a few hours; Merchant Center can take a day to show them.",
        ["Status.Shown"] = "Shown on Google",
        ["Status.Shown.Text"] = "{0} of {1} products are approved or limited.",
        ["Status.Approved"] = "Approved",
        ["Status.Limited"] = "Limited",
        ["Status.Disapproved"] = "Disapproved",
        ["Status.Pending"] = "Pending",
        ["Status.Issues"] = "What Google wants fixed",
        ["Status.Issues.Text"] = "Google's own reasons, most serious first. Fix the product in nopCommerce: the feed or the next sync sends the change, and Google reviews it again.",
        ["Status.Severity.DISAPPROVED"] = "Blocks",
        ["Status.Severity.DEMOTED"] = "Limits",
        ["Status.Severity.OTHER"] = "Advice",
        ["Status.HowToFix"] = "How to fix it",
        ["Status.Products"] = "{0} products",
        ["Status.List"] = "Products",
        ["Status.Search"] = "Name or item ID",
        ["Status.ClearFilter"] = "Show all",
        ["Status.Count"] = "{0} products",
        ["Status.NoProducts"] = "Merchant Center has no products yet. Add the feed URL in Merchant Center or turn on automatic sync, then refresh in a few hours.",
        ["Status.NoMatch"] = "No products match.",
        ["Status.RefusedIn"] = "Refused in: {0}",
        ["Status.FromFile"] = "feed file or another source",

        ["Panel.Title"] = "Google Merchant Center",
        ["Panel.Connect"] = "Connect to Google to see this product's status in Merchant Center.",
        ["Panel.NotInGoogle"] = "Merchant Center doesn't have this product. It may be held back, filtered out of every feed, or not sent yet.",
        ["Panel.Preview"] = "See why in the preview",
        ["Panel.ShownIn"] = "Shown in: {0}",
        ["Panel.FromApi"] = "automatic sync",
        ["Panel.ReadOn"] = "Read from Google {0}."
    };
}
