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
using Nop.Core.Domain.Cms;
using Nop.Services.Cms;
using Nop.Web.Framework.Infrastructure;

namespace Cartenvo.Plugin.Feed.MerchantCenter;

public class MerchantCenterPlugin(ISettingService settings, ILocalizationService localization,
    IPermissionService permissions, IWebHelper webHelper, IScheduleTaskService tasks, INopFileProvider files,
    WidgetSettings widgetSettings)
    : BasePlugin, IMiscPlugin, IWidgetPlugin
{
    // The Google status panel on the admin product page (release 3).
    public bool HideInWidgetList => true;

    public Task<IList<string>> GetWidgetZonesAsync() => Task.FromResult<IList<string>>([AdminWidgetZones.ProductDetailsBlock]);

    public Type GetWidgetViewComponent(string widgetZone) => typeof(Components.GoogleStatusPanelViewComponent);

    /// <summary>nopCommerce only shows widgets that are switched on; the panel is part of the plugin, so it always is.</summary>
    private async Task ActivateWidgetAsync()
    {
        if (widgetSettings.ActiveWidgetSystemNames.Contains(MerchantCenterDefaults.SystemName))
            return;
        widgetSettings.ActiveWidgetSystemNames.Add(MerchantCenterDefaults.SystemName);
        await settings.SaveSettingAsync(widgetSettings);
    }

    public override string GetConfigurationPageUrl() => $"{webHelper.GetStoreLocation()}Admin/MerchantCenter/Configure";

    public override async Task InstallAsync()
    {
        await settings.SaveSettingAsync(new MerchantCenterSettings());
        await localization.AddOrUpdateLocaleResourceAsync(MerchantCenterResources.All());
        await EnsureTasksAsync();
        await ActivateWidgetAsync();
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
        await ActivateWidgetAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    private async Task EnsureTasksAsync()
    {
        foreach (var (name, type, seconds) in new[]
        {
            (MerchantCenterDefaults.TaskName, MerchantCenterDefaults.TaskType, MerchantCenterDefaults.TaskIntervalSeconds),
            (MerchantCenterDefaults.SyncTaskName, MerchantCenterDefaults.SyncTaskType, MerchantCenterDefaults.SyncTaskIntervalSeconds),
            (MerchantCenterDefaults.StatusTaskName, MerchantCenterDefaults.StatusTaskType, MerchantCenterDefaults.StatusTaskIntervalSeconds)
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
        foreach (var type in new[] { MerchantCenterDefaults.TaskType, MerchantCenterDefaults.SyncTaskType, MerchantCenterDefaults.StatusTaskType })
            if (await tasks.GetTaskByTypeAsync(type) is { } task)
                await tasks.DeleteTaskAsync(task);
        // nopCommerce reverses this assembly's migrations after UninstallAsync, which drops the plugin's tables.
        await settings.DeleteSettingAsync<MerchantCenterSettings>();
        await localization.DeleteLocaleResourcesAsync(MerchantCenterDefaults.ResourcePrefix);
        if (widgetSettings.ActiveWidgetSystemNames.Remove(MerchantCenterDefaults.SystemName))
            await settings.SaveSettingAsync(widgetSettings);
        foreach (var permission in new MerchantCenterPermissions().AllConfigs)
            await permissions.DeletePermissionAsync(permission.SystemName);
        var folder = files.MapPath(MerchantCenterDefaults.DataFolder);
        if (files.DirectoryExists(folder))
            files.DeleteDirectory(folder);
        await base.UninstallAsync();
    }
}
