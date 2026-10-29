using Cartenvo.Plugin.Feed.MerchantCenter.Extensibility;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Controllers;

/// <summary>What the paid Pro add-on adds, with a link to its page. Shown only while Pro isn't installed.</summary>
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
public class MerchantCenterProInfoController(IEnumerable<IAdminTabProvider> tabProviders) : BasePluginController
{
    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Index()
    {
        // With Pro installed, its own pages take this tab's place.
        foreach (var provider in tabProviders)
            foreach (var tab in await provider.GetTabsAsync())
                if (tab.Key == ProInfo.TabKey)
                    return RedirectToAction(tab.Action, tab.Controller);

        return View(MerchantCenterDefaults.ViewPath + "ProInfo.cshtml");
    }
}

public static class ProInfo
{
    /// <summary>The tab key Pro uses. While no add-on supplies it, the free plugin shows its own page about Pro there.</summary>
    public const string TabKey = "pro";
}
