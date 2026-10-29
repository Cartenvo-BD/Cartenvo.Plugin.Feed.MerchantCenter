using Nop.Services.ScheduleTasks;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>Runs every hour and rebuilds the enabled feed files that are due. API feeds are synced by <see cref="MerchantSyncTask"/>.</summary>
public class MerchantCenterTask(MerchantFeedService feedService, FeedGenerator generator,
    Nop.Services.Configuration.ISettingService settingService) : IScheduleTask
{
    public async Task ExecuteAsync()
    {
        var settings = await settingService.LoadSettingAsync<MerchantCenterSettings>();
        var every = TimeSpan.FromHours(Math.Clamp(settings.RegenerateEveryHours, 1, 168));
        foreach (var feed in await feedService.GetFeedsAsync())
        {
            // A few minutes of slack so a feed built at 09:01 is rebuilt by the 15:00 run, not the 16:00 one.
            if (!feed.Enabled || feed.IsApi || feed.LastGeneratedOnUtc > DateTime.UtcNow - every + TimeSpan.FromMinutes(5))
                continue;
            await generator.GenerateAsync(feed);
        }
    }
}
