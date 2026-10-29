using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;
using Cartenvo.Plugin.Feed.MerchantCenter.Infrastructure;
#if NOP_500
using Nop.Web.Framework.Extensions;
#else
using Nop.Core.Infrastructure;
using Nop.Services.Localization;
#endif

namespace Cartenvo.Plugin.Feed.MerchantCenter.Data;

// NoMatter runs on fresh installation and upgrade. Reversal on uninstall drops the plugin's tables.
[NopMigration("2026/10/28 20:00:00:0000000", "Merchant Center feed tables", MigrationProcessType.NoMatter)]
public class SchemaMigration : AutoReversingMigration
{
    public override void Up()
    {
        Table<MerchantFeed>();
        Table<MerchantCategoryMapping>();
        Table<MerchantFeedIssue>();
        Table<MerchantProductSetting>();

        Create.Index("IX_MerchantCategoryMapping_Category").OnTable(nameof(MerchantCategoryMapping))
            .OnColumn(nameof(MerchantCategoryMapping.CategoryId)).Ascending();
        Create.Index("IX_MerchantFeedIssue_Feed_Code").OnTable(nameof(MerchantFeedIssue))
            .OnColumn(nameof(MerchantFeedIssue.FeedId)).Ascending()
            .OnColumn(nameof(MerchantFeedIssue.Code)).Ascending();
        Create.Index("IX_MerchantProductSetting_Product").OnTable(nameof(MerchantProductSetting))
            .OnColumn(nameof(MerchantProductSetting.ProductId)).Ascending();
    }

    private void Table<T>() where T : Nop.Core.BaseEntity => Table<T>(this);

    internal static void Table<T>(MigrationBase migration) where T : Nop.Core.BaseEntity
    {
#if NOP_500
        migration.CreateTableIfNotExists<T>(); // Create.TableFor was removed in 5.00
#else
        migration.Create.TableFor<T>();
#endif
    }
}

/// <summary>
/// Release 2: the Merchant API sync. A fresh install already has the new columns (SchemaMigration maps the current
/// entity), so every step checks first.
/// </summary>
[NopMigration("2026/10/29 09:00:00:0000000", "Merchant Center sync tables", MigrationProcessType.NoMatter)]
public class SyncSchemaMigration : Migration
{
    public override void Up()
    {
        var feed = Schema.Table(nameof(MerchantFeed));
        if (!feed.Column(nameof(MerchantFeed.SyncMode)).Exists())
            Alter.Table(nameof(MerchantFeed)).AddColumn(nameof(MerchantFeed.SyncMode)).AsInt32().NotNullable().SetExistingRowsTo(0);
        if (!feed.Column(nameof(MerchantFeed.LastFullSyncOnUtc)).Exists())
            Alter.Table(nameof(MerchantFeed)).AddColumn(nameof(MerchantFeed.LastFullSyncOnUtc)).AsDateTime2().Nullable();
        if (!feed.Column(nameof(MerchantFeed.LastSyncOnUtc)).Exists())
            Alter.Table(nameof(MerchantFeed)).AddColumn(nameof(MerchantFeed.LastSyncOnUtc)).AsDateTime2().Nullable();
        if (!feed.Column(nameof(MerchantFeed.LastSyncError)).Exists())
            Alter.Table(nameof(MerchantFeed)).AddColumn(nameof(MerchantFeed.LastSyncError)).AsString(1000).Nullable();

        if (!Schema.Table(nameof(MerchantSyncQueueItem)).Exists())
        {
            SchemaMigration.Table<MerchantSyncQueueItem>(this);
            Create.Index("IX_MerchantSyncQueueItem_Product").OnTable(nameof(MerchantSyncQueueItem))
                .OnColumn(nameof(MerchantSyncQueueItem.ProductId)).Ascending();
        }
        if (!Schema.Table(nameof(MerchantSyncedItem)).Exists())
        {
            SchemaMigration.Table<MerchantSyncedItem>(this);
            Create.Index("IX_MerchantSyncedItem_Feed_Product").OnTable(nameof(MerchantSyncedItem))
                .OnColumn(nameof(MerchantSyncedItem.FeedId)).Ascending()
                .OnColumn(nameof(MerchantSyncedItem.ProductId)).Ascending();
            Create.Index("IX_MerchantSyncedItem_Feed_Offer").OnTable(nameof(MerchantSyncedItem))
                .OnColumn(nameof(MerchantSyncedItem.FeedId)).Ascending()
                .OnColumn(nameof(MerchantSyncedItem.OfferId)).Ascending();
        }
    }

    // Runs on uninstall before SchemaMigration is reversed, which drops MerchantFeed with its new columns.
    public override void Down()
    {
        if (Schema.Table(nameof(MerchantSyncedItem)).Exists())
            Delete.Table(nameof(MerchantSyncedItem));
        if (Schema.Table(nameof(MerchantSyncQueueItem)).Exists())
            Delete.Table(nameof(MerchantSyncQueueItem));
    }
}

/// <summary>Release 3: product status read back from Google.</summary>
// nopCommerce tells migrations apart by date alone, across all plugins: keep the time unusual.
[NopMigration("2026/10/30 09:41:27:3814000", "Merchant Center status table", MigrationProcessType.NoMatter)]
public class StatusSchemaMigration : Migration
{
    public override void Up()
    {
        if (Schema.Table(nameof(MerchantGoogleStatus)).Exists())
            return;
        SchemaMigration.Table<MerchantGoogleStatus>(this);
        Create.Index("IX_MerchantGoogleStatus_Product").OnTable(nameof(MerchantGoogleStatus))
            .OnColumn(nameof(MerchantGoogleStatus.ProductId)).Ascending();
        Create.Index("IX_MerchantGoogleStatus_Status").OnTable(nameof(MerchantGoogleStatus))
            .OnColumn(nameof(MerchantGoogleStatus.Status)).Ascending();
    }

    public override void Down()
    {
        if (Schema.Table(nameof(MerchantGoogleStatus)).Exists())
            Delete.Table(nameof(MerchantGoogleStatus));
    }
}

/// <summary>Adds the English texts on installation. Upgrades add new texts in MerchantCenterPlugin.UpdateAsync.</summary>
[NopMigration("2026/10/28 20:01:00:0000000", "Merchant Center feed resources", MigrationProcessType.NoMatter)]
public class ResourcesMigration : MigrationBase
{
    public override void Up()
    {
#if NOP_500
        this.AddOrUpdateLocaleResource(MerchantCenterResources.All());
#else
        EngineContext.Current.Resolve<ILocalizationService>().AddOrUpdateLocaleResource(MerchantCenterResources.All());
#endif
    }

    public override void Down() { } // Uninstall removes resources by prefix.
}
