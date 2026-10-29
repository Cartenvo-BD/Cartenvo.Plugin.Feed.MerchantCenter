using Cartenvo.MerchantCenter.Core.MerchantApi;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
using Cartenvo.Plugin.Feed.MerchantCenter.Models;
using Cartenvo.Plugin.Feed.MerchantCenter.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Controllers;

/// <summary>The Google connection page: service account key, registration and the plugin's data source.</summary>
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class MerchantCenterSyncController(MerchantApiFactory apiFactory,
    MerchantFeedService feedService,
    MerchantSyncStore store,
    FeedCards cards,
    ISettingService settingService,
    ILocalizationService localization,
    IProductService productService,
    IWorkContext workContext) : BasePluginController
{
    private const string P = MerchantCenterDefaults.ResourcePrefix;

    [HttpGet]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Connection()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        var customer = await workContext.GetCurrentCustomerAsync();
        var model = new ConnectionModel
        {
            MerchantId = settings.ApiMerchantId,
            ClientEmail = settings.ApiClientEmail,
            ProjectId = settings.ApiProjectId,
            AccountName = settings.ApiAccountName,
            KeySaved = !string.IsNullOrEmpty(settings.ApiKeyEncrypted),
            KeyReadable = apiFactory.ReadKey(settings) != null,
            Registered = settings.ApiRegistered,
            DataSource = settings.ApiDataSource,
            AdminEmail = customer?.Email,
            Queued = await store.CountQueuedAsync()
        };
        foreach (var feed in await feedService.GetFeedsAsync())
            model.Feeds.Add(await cards.CardAsync(feed));

        var feedNames = model.Feeds.ToDictionary(card => card.Feed.Id, card => card.Feed.Name);
        var errors = await store.GetErrorsAsync(0, 50);
        var products = (await productService.GetProductsByIdsAsync(errors.Select(e => e.ProductId).Distinct().ToArray()))
            .ToDictionary(p => p.Id);
        foreach (var error in errors)
            model.Errors.Add(new SyncErrorModel
            {
                ProductId = error.ProductId,
                ProductName = products.TryGetValue(error.ProductId, out var product) ? product.Name : $"#{error.ProductId}",
                OfferId = error.OfferId,
                FeedName = feedNames.GetValueOrDefault(error.FeedId, "—"),
                Error = error.Error,
                OnUtc = error.LastAttemptOnUtc,
                Removing = !feedNames.ContainsKey(error.FeedId)
            });
        return View(MerchantCenterDefaults.ViewPath + "Connection.cshtml", model);
    }

    /// <summary>Saves the key and account ID, then checks the key with Google and tries the account.</summary>
    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> SaveKey(string merchantId, string keyJson, IFormFile keyFile)
    {
        merchantId = new string((merchantId ?? "").Where(char.IsDigit).ToArray());
        if (keyFile is { Length: > 0 and < 100_000 })
        {
            using var reader = new StreamReader(keyFile.OpenReadStream());
            keyJson = await reader.ReadToEndAsync();
        }
        var settings = await apiFactory.LoadSettingsAsync();
        if (merchantId.Length == 0)
            return await FailAsync("Connection.Error.MerchantId");

        ServiceAccountKey key;
        if (string.IsNullOrWhiteSpace(keyJson))
        {
            // Changing only the account ID keeps the saved key.
            key = apiFactory.ReadKey(settings);
            if (key == null)
                return await FailAsync("Connection.Error.Key.empty");
        }
        else
        {
            key = ServiceAccountKey.TryParse(keyJson, out var keyError);
            if (key == null)
                return await FailAsync("Connection.Error.Key." + keyError);
        }

        var client = apiFactory.Create(key, merchantId, settings.ApiBaseUrl);
        var account = await client.GetAccountAsync();
        var problem = account.Problem;
        if (problem is ApiProblems.BadKey or ApiProblems.Network)
            return await FailAsync("Connection.Problem." + problem, account.Message);

        var changed = settings.ApiMerchantId != merchantId || settings.ApiClientEmail != key.ClientEmail || settings.ApiProjectId != key.ProjectId;
        if (!string.IsNullOrWhiteSpace(keyJson))
            settings.ApiKeyEncrypted = apiFactory.Encrypt(keyJson.Trim());
        settings.ApiMerchantId = merchantId;
        settings.ApiClientEmail = key.ClientEmail;
        settings.ApiProjectId = key.ProjectId;
        if (changed)
        {
            // Another account or project: register and connect again.
            settings.ApiRegistered = false;
            settings.ApiDataSource = "";
            settings.ApiAccountName = "";
        }
        if (account.Ok)
        {
            settings.ApiRegistered = true; // Calls only work for registered projects.
            settings.ApiAccountName = AccountName(account);
        }
        await settingService.SaveSettingAsync(settings);
        MerchantApiFactory.ClearTokens();

        var next = account.Ok ? "Connection.KeySaved.Ready" : "Connection.KeySaved.Next";
        return Json(new
        {
            ok = true,
            message = await localization.GetResourceAsync(P + next),
            email = key.ClientEmail,
            problem,
            detail = account.Ok ? null : await ExplainAsync(account)
        });
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Register(string email)
    {
        var settings = await apiFactory.LoadSettingsAsync();
        var client = apiFactory.Create(settings);
        if (client == null)
            return await FailAsync("Connection.Error.Key.empty");
        var result = await client.RegisterGcpAsync(email);
        if (!result.Ok)
            return await FailAsync("Connection.Problem." + result.Problem, result.Message);
        settings.ApiRegistered = true;
        await settingService.SaveSettingAsync(settings);
        return Json(new { ok = true, message = await localization.GetResourceAsync(P + "Connection.Registered") });
    }

    /// <summary>Checks the account, then creates the plugin's API data source (or reuses the saved one).</summary>
    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Connect()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        var client = apiFactory.Create(settings);
        if (client == null)
            return await FailAsync("Connection.Error.Key.empty");

        var account = await client.GetAccountAsync();
        if (!account.Ok)
            return await FailAsync("Connection.Problem." + account.Problem, account.Message);
        settings.ApiAccountName = AccountName(account);
        settings.ApiRegistered = true;

        var dataSource = settings.ApiDataSource;
        if (!string.IsNullOrEmpty(dataSource) && !(await client.GetDataSourceAsync(dataSource)).Ok)
            dataSource = "";
        if (string.IsNullOrEmpty(dataSource))
        {
            var created = await client.CreateDataSourceAsync(await localization.GetResourceAsync(P + "Connection.DataSourceName"));
            dataSource = created.Json?["name"]?.GetValue<string>();
            if (!created.Ok || string.IsNullOrEmpty(dataSource))
                return await FailAsync("Connection.Problem." + created.Problem, created.Message);
        }
        if (dataSource != settings.ApiDataSource)
        {
            // A new data source starts empty: send every product to it on the next run.
            await store.ForgetAllSentAsync();
            foreach (var feed in await feedService.GetFeedsAsync())
                if (feed.IsApi)
                {
                    feed.LastFullSyncOnUtc = null;
                    await feedService.UpdateFeedAsync(feed);
                }
        }
        settings.ApiDataSource = dataSource;
        await settingService.SaveSettingAsync(settings);
        return Json(new { ok = true, message = string.Format(await localization.GetResourceAsync(P + "Connection.Connected"), settings.ApiAccountName) });
    }

    /// <summary>Turns automatic sync on or off for one feed.</summary>
    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> SetMode(int feedId, bool api)
    {
        var settings = await apiFactory.LoadSettingsAsync();
        if (await feedService.GetFeedAsync(feedId) is { } feed && (!api || settings.ApiConnected))
            await SetModeAsync(feed, api);
        return RedirectToAction(nameof(Connection));
    }

    internal async Task SetModeAsync(MerchantFeed feed, bool api)
    {
        var mode = (int)(api ? FeedSyncMode.Api : FeedSyncMode.File);
        if (feed.SyncMode == mode)
            return;
        feed.SyncMode = mode;
        feed.LastFullSyncOnUtc = null; // The next run compares every product.
        feed.LastSyncError = null;
        await feedService.UpdateFeedAsync(feed);
        MerchantSyncStore.ForgetApiFeedCheck();
    }

    [HttpPost]
    [CheckPermission(MerchantCenterPermissions.Manage)]
    public async Task<IActionResult> Disconnect()
    {
        var settings = await apiFactory.LoadSettingsAsync();
        settings.ApiKeyEncrypted = "";
        settings.ApiClientEmail = "";
        settings.ApiProjectId = "";
        settings.ApiAccountName = "";
        settings.ApiRegistered = false;
        settings.ApiDataSource = "";
        await settingService.SaveSettingAsync(settings);
        MerchantApiFactory.ClearTokens();
        // Feeds go back to feed files, which work without a connection.
        foreach (var feed in await feedService.GetFeedsAsync())
            if (feed.IsApi)
                await SetModeAsync(feed, false);
        return RedirectToAction(nameof(Connection));
    }

    private async Task<IActionResult> FailAsync(string resource, string googleMessage = null)
    {
        var text = await localization.GetResourceAsync(P + resource);
        if (string.IsNullOrEmpty(text) || text == P + resource || text.StartsWith(P, StringComparison.Ordinal))
            text = await localization.GetResourceAsync(P + "Connection.Problem.other");
        return Json(new { ok = false, message = text, google = googleMessage });
    }

    private async Task<string> ExplainAsync(ApiResult result)
        => await localization.GetResourceAsync(P + "Connection.Problem." + result.Problem);

    private static string AccountName(ApiResult account)
        => account.Json?["accountName"]?.GetValue<string>() ?? account.Json?["accountId"]?.ToString() ?? "";
}
