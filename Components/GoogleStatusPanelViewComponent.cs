using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Cartenvo.Plugin.Feed.MerchantCenter.Models;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Security;
using Nop.Web.Framework.Components;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Components;

/// <summary>"Google Merchant Center" panel on the admin product edit page: the product's status and Google's issues.</summary>
public class GoogleStatusPanelViewComponent(GoogleStatusService statusService, MerchantApiFactory apiFactory, IPermissionService permissions)
    : NopViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        // additionalData is the admin ProductModel; read its Id without referencing Nop.Web.
        var id = additionalData?.GetType().GetProperty("Id")?.GetValue(additionalData) as int? ?? 0;
        if (id <= 0 || !await permissions.AuthorizeAsync(MerchantCenterPermissions.Manage))
            return Content("");
        var settings = await apiFactory.LoadSettingsAsync();
        var model = new ProductStatusPanelModel { CanRead = settings.ApiCanRead, ProductId = id, ReadOnUtc = settings.StatusReadOnUtc };
        if (model.CanRead && model.ReadOnUtc != null)
            foreach (var row in await statusService.ForProductAsync(id))
                model.Rows.Add(new StatusRowModel
                {
                    ProductId = id,
                    OfferId = row.OfferId,
                    FeedLabel = row.FeedLabel,
                    ContentLanguage = row.ContentLanguage,
                    Status = (GoogleStatus)row.Status,
                    ApprovedCountries = row.ApprovedCountries,
                    DisapprovedCountries = row.DisapprovedCountries,
                    Issues = GoogleStatusService.Issues(row.IssuesJson),
                    FromApi = row.FromApi,
                    ReadOnUtc = row.ReadOnUtc
                });
        return View(MerchantCenterDefaults.ViewPath + "ProductPanel.cshtml", model);
    }
}
