using Cartenvo.MerchantCenter.Core;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Nop.Core.Infrastructure;
using Nop.Services.Logging;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>
/// Google's product taxonomy, downloaded from Google once and cached in App_Data. The categories are only used to
/// pick a Google category in the admin; feeds send the numeric ID.
/// </summary>
public class GoogleTaxonomyService(IHttpClientFactory httpClientFactory, INopFileProvider files, ILogger logger)
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static TaxonomyIndex _index;
    private static DateTime _failedUntilUtc;

    /// <summary>The taxonomy, or null when it isn't cached and Google can't be reached right now.</summary>
    public async Task<TaxonomyIndex> GetAsync()
    {
        if (_index != null)
            return _index;
        await Lock.WaitAsync();
        try
        {
            if (_index != null)
                return _index;
            var path = files.Combine(files.MapPath(MerchantCenterDefaults.DataFolder), "google-taxonomy.en-US.txt");
            if (!files.FileExists(path) || new FileInfo(path).Length < 10000)
            {
                if (DateTime.UtcNow < _failedUntilUtc)
                    return null;
                if (!await DownloadAsync(path))
                {
                    _failedUntilUtc = DateTime.UtcNow.AddMinutes(10);
                    return null;
                }
            }
            using var reader = new StreamReader(path);
            _index = TaxonomyIndex.Parse(reader);
            return _index;
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<bool> DownloadAsync(string path)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var text = await client.GetStringAsync(MerchantCenterDefaults.TaxonomyUrl);
            if (!text.Contains(" - ", StringComparison.Ordinal))
                return false;
            files.CreateDirectory(files.GetDirectoryName(path));
            await File.WriteAllTextAsync(path + ".tmp", text);
            File.Move(path + ".tmp", path, true);
            return true;
        }
        catch (Exception ex)
        {
            await logger.WarningAsync("Merchant Center feed: couldn't download Google's product categories. Category names will show once the server can reach google.com.", ex);
            return false;
        }
    }
}
