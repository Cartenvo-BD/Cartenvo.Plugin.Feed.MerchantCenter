using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Customers;
using Nop.Core.Infrastructure;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Menu;
using Nop.Web.Framework.Mvc.Routing;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;

public class MerchantCenterStartup : INopStartup
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<MerchantFeedService>();
        services.AddScoped<GoogleTaxonomyService>();
        services.AddScoped<FeedBuilder>();
        services.AddScoped<FeedGenerator>();
        services.AddScoped<MerchantApiFactory>();
        services.AddScoped<MerchantSyncStore>();
        services.AddScoped<MerchantSyncService>();
        services.AddScoped<FeedCards>();
        services.AddHttpClient(MerchantCenterDefaults.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Cartenvo-MerchantCenter-nopCommerce/2");
        });
    }

    public void Configure(IApplicationBuilder application) { }
    public int Order => 3000;
}

/// <summary>The public feed URL that Merchant Center fetches.</summary>
public class MerchantCenterRouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        => endpointRouteBuilder.MapControllerRoute(MerchantCenterDefaults.FeedRouteName,
            "merchant-center/feed/{feedId:int}/{token}.xml",
            new { controller = "MerchantCenterFeed", action = "Feed" });

    public int Priority => 0;
}

public class MerchantCenterPermissions : IPermissionConfigManager
{
    public const string Manage = "CartenvoMerchantCenter.Manage";

    public IList<PermissionConfig> AllConfigs =>
    [
        new("Admin area. Manage Merchant Center feeds", Manage, "Promotions", NopCustomerDefaults.AdministratorsRoleName)
    ];
}

public class MerchantCenterMenuConsumer(IPluginManager<IPlugin> pluginManager,
    IPermissionService permissions, ILocalizationService localization)
    : BaseAdminMenuCreatedEventConsumer(pluginManager)
{
    protected override string PluginSystemName => MerchantCenterDefaults.SystemName;
    protected override Task<bool> CheckAccessAsync() => permissions.AuthorizeAsync(MerchantCenterPermissions.Manage);

    protected override async Task<AdminMenuItem> GetAdminMenuItemAsync(IPlugin plugin) => new()
    {
        SystemName = PluginSystemName,
        Title = await localization.GetResourceAsync(MerchantCenterDefaults.ResourcePrefix + "Menu"),
        IconClass = "far fa-dot-circle",
        Visible = true,
        Url = plugin.GetConfigurationPageUrl()
    };

    // Promotions → after Campaigns. nopCommerce inserts before a named item unless told otherwise.
    protected override MenuItemInsertType InsertType => MenuItemInsertType.After;
    protected override string AfterMenuSystemName => "Campaigns";
}
