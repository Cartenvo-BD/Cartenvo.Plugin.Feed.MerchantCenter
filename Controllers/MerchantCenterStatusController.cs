using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Cartenvo.Plugin.Feed.MerchantCenter.Models;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Controllers;

/// <summary>"Products in Google": approval status and Google's issues, read back from Merchant Center.</summary>
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class MerchantCenterStatusController(GoogleStatusService statusService,
    MerchantApiFactory apiFactory,
    IProductService productService,
    IPictureService pictureService,
    ILocalizationService localization) : BasePluginController
{
    private const int PageSize = 50;

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Index(int? status = null, string issue = null, string q = null, int page = 1)
    {
        var settings = await apiFactory.LoadSettingsAsync();
        var model = new StatusPageModel
        {
            CanRead = settings.ApiCanRead,
            ReadOnUtc = settings.StatusReadOnUtc,
            Error = settings.StatusReadError,
            EveryHours = settings.StatusEveryHours,
            Running = GoogleStatusService.IsRunning,
            Status = status is >= 0 and <= 3 ? status : null,
            Issue = issue,
            Search = q,
            Page = Math.Max(1, page)
        };
        if (!model.CanRead || model.ReadOnUtc == null)
            return View(MerchantCenterDefaults.ViewPath + "Status.cshtml", model);

        model.Counts = await statusService.CountsAsync();
        model.Issues = await statusService.IssueGroupsAsync();
        var (rows, total) = await statusService.SearchAsync(model.Status, issue, q, model.Page - 1, PageSize);
        model.Total = total;
        model.TotalPages = (total + PageSize - 1) / PageSize;
        var products = (await productService.GetProductsByIdsAsync(rows.Where(r => r.ProductId > 0).Select(r => r.ProductId).Distinct().ToArray()))
            .ToDictionary(p => p.Id);
        foreach (var row in rows)
        {
            products.TryGetValue(row.ProductId, out var product);
            model.Rows.Add(new StatusRowModel
            {
                ProductId = product?.Id ?? 0,
                Name = product?.Name,
                OfferId = row.OfferId,
                FeedLabel = row.FeedLabel,
                ContentLanguage = row.ContentLanguage,
                Status = (GoogleStatus)row.Status,
                ApprovedCountries = row.ApprovedCountries,
                DisapprovedCountries = row.DisapprovedCountries,
                Issues = GoogleStatusService.Issues(row.IssuesJson),
                FromApi = row.FromApi,
                ImageUrl = product == null ? null : await ThumbnailAsync(product.Id)
            });
        }
        return View(MerchantCenterDefaults.ViewPath + "Status.cshtml", model);
    }

    /// <summary>Reads statuses from Google now. Large accounts take a minute or two.</summary>
    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Refresh()
    {
        var result = await statusService.RefreshAsync(HttpContext.RequestAborted);
        var message = result.Success
            ? string.Format(await localization.GetResourceAsync(MerchantCenterDefaults.ResourcePrefix + "Status.Read"), result.Products.ToString("N0"), result.Matched.ToString("N0"))
            : result.Error;
        return Json(new { ok = result.Success, message });
    }

    private async Task<string> ThumbnailAsync(int productId)
    {
        var picture = (await pictureService.GetPicturesByProductIdAsync(productId, 1)).FirstOrDefault();
        return picture == null
            ? await pictureService.GetDefaultPictureUrlAsync(80)
            : await pictureService.GetPictureUrlAsync(picture.Id, 80);
    }
}
