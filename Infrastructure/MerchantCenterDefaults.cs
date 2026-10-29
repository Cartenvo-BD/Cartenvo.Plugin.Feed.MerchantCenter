namespace Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;

public static class MerchantCenterDefaults
{
    public const string SystemName = "Feed.CartenvoMerchantCenter";
    public const string ResourcePrefix = "Plugins.Feed.CartenvoMerchantCenter.";
    public const string ViewPath = "~/Plugins/Feed.CartenvoMerchantCenter/Views/";

    /// <summary>Feeds and the taxonomy cache live here, outside wwwroot, so they're only served through the token URL.</summary>
    public const string DataFolder = "~/App_Data/CartenvoMerchantCenter";

    public const string FeedRouteName = "Plugin.Feed.CartenvoMerchantCenter.Feed";
    public const string TaskName = "Merchant Center feeds (Cartenvo)";
    public const string TaskType = "Cartenvo.Plugin.Feed.MerchantCenter.Services.MerchantCenterTask";

    /// <summary>The scheduled task checks every hour which feeds are due.</summary>
    public const int TaskIntervalSeconds = 3600;

    public const string SyncTaskName = "Merchant Center sync (Cartenvo)";
    public const string SyncTaskType = "Cartenvo.Plugin.Feed.MerchantCenter.Services.MerchantSyncTask";

    /// <summary>Changed products reach Google within about this long.</summary>
    public const int SyncTaskIntervalSeconds = 300;

    public const string HttpClientName = "CartenvoMerchantApi";

    public const string CloudConsoleUrl = "https://console.cloud.google.com/";
    public const string EnableApiUrl = "https://console.cloud.google.com/apis/library/merchantapi.googleapis.com";
    public const string ServiceAccountsUrl = "https://console.cloud.google.com/iam-admin/serviceaccounts";
    public const string PeopleAndAccessUrl = "https://merchants.google.com/mc/users";

    public const string TaxonomyUrl = "https://www.google.com/basepages/producttype/taxonomy-with-ids.en-US.txt";

    /// <summary>Most problem rows kept per feed. The counts on the overview stay exact.</summary>
    public const int MaxStoredIssues = 20000;

    public const int PageSize = 500;

    public const string HelpUrl = "https://cartenvo.com/plugins/merchant-center?utm_source=plugin&utm_medium=admin&utm_campaign=merchant-center";
    public const string ServicesUrl = "https://cartenvo.com/services?utm_source=plugin&utm_medium=admin&utm_campaign=merchant-center";
    public const string GitHubUrl = "https://github.com/Cartenvo-BD/Cartenvo.Plugin.Feed.MerchantCenter";
    public const string MerchantCenterUrl = "https://merchants.google.com/";
}
