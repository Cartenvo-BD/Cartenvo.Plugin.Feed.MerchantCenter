using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;
using Cartenvo.Plugin.Feed.MerchantCenter.Domain;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Data;

public class MerchantFeedBuilder : NopEntityBuilder<MerchantFeed>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(MerchantFeed.Name)).AsString(200).NotNullable()
        .WithColumn(nameof(MerchantFeed.CountryCode)).AsString(2).NotNullable()
        .WithColumn(nameof(MerchantFeed.Token)).AsString(64).NotNullable()
        .WithColumn(nameof(MerchantFeed.CategoryIds)).AsString(4000).Nullable()
        .WithColumn(nameof(MerchantFeed.MinPrice)).AsDecimal(18, 4).Nullable()
        .WithColumn(nameof(MerchantFeed.LastError)).AsString(1000).Nullable()
        .WithColumn(nameof(MerchantFeed.IssueSummaryJson)).AsString(4000).Nullable()
        .WithColumn(nameof(MerchantFeed.LastSyncError)).AsString(1000).Nullable();
}

public class MerchantCategoryMappingBuilder : NopEntityBuilder<MerchantCategoryMapping>
{
    public override void MapEntity(CreateTableExpressionBuilder table) { }
}

public class MerchantFeedIssueBuilder : NopEntityBuilder<MerchantFeedIssue>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(MerchantFeedIssue.OfferId)).AsString(100).Nullable()
        .WithColumn(nameof(MerchantFeedIssue.Code)).AsString(50).NotNullable();
}

public class MerchantProductSettingBuilder : NopEntityBuilder<MerchantProductSetting>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(MerchantProductSetting.Condition)).AsString(20).Nullable();
}

public class MerchantSyncQueueItemBuilder : NopEntityBuilder<MerchantSyncQueueItem>
{
    public override void MapEntity(CreateTableExpressionBuilder table) { }
}

public class MerchantGoogleStatusBuilder : NopEntityBuilder<MerchantGoogleStatus>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(MerchantGoogleStatus.OfferId)).AsString(150).NotNullable()
        .WithColumn(nameof(MerchantGoogleStatus.ContentLanguage)).AsString(10).NotNullable()
        .WithColumn(nameof(MerchantGoogleStatus.FeedLabel)).AsString(50).NotNullable()
        .WithColumn(nameof(MerchantGoogleStatus.ApprovedCountries)).AsString(1000).Nullable()
        .WithColumn(nameof(MerchantGoogleStatus.DisapprovedCountries)).AsString(1000).Nullable()
        .WithColumn(nameof(MerchantGoogleStatus.IssuesJson)).AsString(int.MaxValue).Nullable()
        .WithColumn(nameof(MerchantGoogleStatus.IssueCodes)).AsString(1000).Nullable();
}

public class MerchantSyncedItemBuilder : NopEntityBuilder<MerchantSyncedItem>
{
    public override void MapEntity(CreateTableExpressionBuilder table) => table
        .WithColumn(nameof(MerchantSyncedItem.OfferId)).AsString(150).NotNullable()
        .WithColumn(nameof(MerchantSyncedItem.ContentLanguage)).AsString(10).NotNullable()
        .WithColumn(nameof(MerchantSyncedItem.FeedLabel)).AsString(20).NotNullable()
        .WithColumn(nameof(MerchantSyncedItem.Hash)).AsString(64).Nullable()
        .WithColumn(nameof(MerchantSyncedItem.Error)).AsString(1000).Nullable();
}
