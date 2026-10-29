using System.Security.Cryptography;
using System.Text;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Controllers;

/// <summary>
/// Serves a built feed to Merchant Center. Only the secret token in the URL gives access; the file is never built
/// here, so a fetch is cheap.
/// </summary>
public class MerchantCenterFeedController(MerchantFeedService feedService, FeedGenerator generator) : Controller
{
    [HttpGet]
    [HttpHead]
    public async Task<IActionResult> Feed(int feedId, string token)
    {
        var feed = await feedService.GetFeedAsync(feedId);
        if (feed == null || string.IsNullOrEmpty(token) || !SameToken(feed.Token, token))
            return NotFound();
        var path = generator.FeedFilePath(feed);
        if (!System.IO.File.Exists(path))
            return NotFound();
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Robots-Tag"] = "noindex";
        return PhysicalFile(path, "application/xml; charset=utf-8", new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path)), null);
    }

    private static bool SameToken(string expected, string actual)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected ?? ""), Encoding.UTF8.GetBytes(actual));
}
