using Nop.Core;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Infrastructure;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;

namespace Cartenvo.Plugin.Feed.MerchantCenter;

public class MerchantCenterPlugin(ISettingService settings, ILocalizationService localization,
    IPermissionService permissions, IWebHelper webHelper, IScheduleTaskService tasks, INopFileProvider files)
    : BasePlugin, IMiscPlugin
{
    public override string GetConfigurationPageUrl() => $"{webHelper.GetStoreLocation()}Admin/MerchantCenter/Configure";

    public override async Task InstallAsync()
    {
        await settings.SaveSettingAsync(new MerchantCenterSettings());
        await localization.AddOrUpdateLocaleResourceAsync(MerchantCenterResources.All());
        await EnsureTasksAsync();
        await base.InstallAsync();
    }

    /// <summary>
    /// Runs when the plugin version changes. nopCommerce applies each migration only once, so new texts are added
    /// here, and upgrading from release 1 adds the sync task.
    /// </summary>
    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await localization.AddOrUpdateLocaleResourceAsync(MerchantCenterResources.All());
        await EnsureTasksAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    private async Task EnsureTasksAsync()
    {
        foreach (var (name, type, seconds) in new[]
        {
            (MerchantCenterDefaults.TaskName, MerchantCenterDefaults.TaskType, MerchantCenterDefaults.TaskIntervalSeconds),
            (MerchantCenterDefaults.SyncTaskName, MerchantCenterDefaults.SyncTaskType, MerchantCenterDefaults.SyncTaskIntervalSeconds)
        })
        {
            if (await tasks.GetTaskByTypeAsync(type) != null)
                continue;
            await tasks.InsertTaskAsync(new ScheduleTask
            {
                Name = name,
                Seconds = seconds,
                Type = type,
                Enabled = true,
                LastEnabledUtc = DateTime.UtcNow,
                StopOnError = false
            });
        }
    }

    public override async Task UninstallAsync()
    {
        foreach (var type in new[] { MerchantCenterDefaults.TaskType, MerchantCenterDefaults.SyncTaskType })
            if (await tasks.GetTaskByTypeAsync(type) is { } task)
                await tasks.DeleteTaskAsync(task);
        // nopCommerce reverses this assembly's migrations after UninstallAsync, which drops the plugin's tables.
        await settings.DeleteSettingAsync<MerchantCenterSettings>();
        await localization.DeleteLocaleResourcesAsync(MerchantCenterDefaults.ResourcePrefix);
        foreach (var permission in new MerchantCenterPermissions().AllConfigs)
            await permissions.DeletePermissionAsync(permission.SystemName);
        var folder = files.MapPath(MerchantCenterDefaults.DataFolder);
        if (files.DirectoryExists(folder))
            files.DeleteDirectory(folder);
        await base.UninstallAsync();
    }
}
