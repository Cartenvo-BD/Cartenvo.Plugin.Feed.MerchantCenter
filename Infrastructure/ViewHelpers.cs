namespace Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;

/// <summary>Small formatting helpers for the admin views.</summary>
public static class ViewHelpers
{
    /// <summary>nopCommerce's own flag image for a country (wwwroot/images/flags).</summary>
    public static string FlagImage(string code)
        => $"~/images/flags/{(string.IsNullOrEmpty(code) ? "un" : code.ToLowerInvariant())}.png";

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB"
    };

    /// <summary>True for addresses Google can't reach, such as localhost.</summary>
    public static bool IsLocal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;
        return uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The store address in a feed URL, without the feed path.</summary>
    public static string StoreAddress(string feedUrl)
    {
        var index = feedUrl.IndexOf("merchant-center/", StringComparison.Ordinal);
        return index > 0 ? feedUrl[..index] : feedUrl;
    }
}
