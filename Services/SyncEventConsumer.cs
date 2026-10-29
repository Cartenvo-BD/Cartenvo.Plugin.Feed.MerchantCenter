using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Seo;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Cartenvo.Plugin.Feed.MerchantCenter.Services;

/// <summary>
/// Queues products whose Google data may have changed, for the sync task to send within minutes. Changes that affect
/// many products (a category's name, a currency rate, a discount) queue a full comparison instead. Nothing is queued
/// while no feed syncs through the API. The daily full sync catches anything these events miss.
/// </summary>
public class SyncEventConsumer(MerchantSyncStore store) :
    IConsumer<EntityInsertedEvent<Product>>, IConsumer<EntityUpdatedEvent<Product>>, IConsumer<EntityDeletedEvent<Product>>,
    IConsumer<EntityInsertedEvent<ProductAttributeCombination>>, IConsumer<EntityUpdatedEvent<ProductAttributeCombination>>, IConsumer<EntityDeletedEvent<ProductAttributeCombination>>,
    IConsumer<EntityInsertedEvent<ProductAttributeMapping>>, IConsumer<EntityDeletedEvent<ProductAttributeMapping>>,
    IConsumer<EntityInsertedEvent<ProductPicture>>, IConsumer<EntityUpdatedEvent<ProductPicture>>, IConsumer<EntityDeletedEvent<ProductPicture>>,
    IConsumer<EntityInsertedEvent<ProductCategory>>, IConsumer<EntityUpdatedEvent<ProductCategory>>, IConsumer<EntityDeletedEvent<ProductCategory>>,
    IConsumer<EntityInsertedEvent<ProductManufacturer>>, IConsumer<EntityUpdatedEvent<ProductManufacturer>>, IConsumer<EntityDeletedEvent<ProductManufacturer>>,
    IConsumer<EntityInsertedEvent<ProductWarehouseInventory>>, IConsumer<EntityUpdatedEvent<ProductWarehouseInventory>>, IConsumer<EntityDeletedEvent<ProductWarehouseInventory>>,
    IConsumer<EntityInsertedEvent<TierPrice>>, IConsumer<EntityUpdatedEvent<TierPrice>>, IConsumer<EntityDeletedEvent<TierPrice>>,
    IConsumer<EntityInsertedEvent<DiscountProductMapping>>, IConsumer<EntityDeletedEvent<DiscountProductMapping>>,
    IConsumer<EntityInsertedEvent<UrlRecord>>, IConsumer<EntityUpdatedEvent<UrlRecord>>,
    IConsumer<EntityInsertedEvent<LocalizedProperty>>, IConsumer<EntityUpdatedEvent<LocalizedProperty>>, IConsumer<EntityDeletedEvent<LocalizedProperty>>,
    IConsumer<EntityUpdatedEvent<Category>>, IConsumer<EntityDeletedEvent<Category>>,
    IConsumer<EntityUpdatedEvent<Manufacturer>>, IConsumer<EntityDeletedEvent<Manufacturer>>,
    IConsumer<EntityUpdatedEvent<Discount>>, IConsumer<EntityDeletedEvent<Discount>>,
    IConsumer<EntityUpdatedEvent<Currency>>
{
    // One product
    public Task HandleEventAsync(EntityInsertedEvent<Product> e) => store.QueueAsync(e.Entity.Id);
    public Task HandleEventAsync(EntityUpdatedEvent<Product> e) => store.QueueAsync(e.Entity.Id);
    public Task HandleEventAsync(EntityDeletedEvent<Product> e) => store.QueueAsync(e.Entity.Id);
    public Task HandleEventAsync(EntityInsertedEvent<ProductAttributeCombination> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<ProductAttributeCombination> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductAttributeCombination> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<ProductAttributeMapping> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductAttributeMapping> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<ProductPicture> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<ProductPicture> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductPicture> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<ProductCategory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<ProductCategory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductCategory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<ProductManufacturer> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<ProductManufacturer> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductManufacturer> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<ProductWarehouseInventory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<ProductWarehouseInventory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<ProductWarehouseInventory> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<TierPrice> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityUpdatedEvent<TierPrice> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityDeletedEvent<TierPrice> e) => store.QueueAsync(e.Entity.ProductId);
    public Task HandleEventAsync(EntityInsertedEvent<DiscountProductMapping> e) => store.QueueAsync(e.Entity.EntityId);
    public Task HandleEventAsync(EntityDeletedEvent<DiscountProductMapping> e) => store.QueueAsync(e.Entity.EntityId);
    public Task HandleEventAsync(EntityInsertedEvent<UrlRecord> e) => ProductOnly(e.Entity.EntityName, e.Entity.EntityId);
    public Task HandleEventAsync(EntityUpdatedEvent<UrlRecord> e) => ProductOnly(e.Entity.EntityName, e.Entity.EntityId);
    public Task HandleEventAsync(EntityInsertedEvent<LocalizedProperty> e) => ProductOnly(e.Entity.LocaleKeyGroup, e.Entity.EntityId);
    public Task HandleEventAsync(EntityUpdatedEvent<LocalizedProperty> e) => ProductOnly(e.Entity.LocaleKeyGroup, e.Entity.EntityId);
    public Task HandleEventAsync(EntityDeletedEvent<LocalizedProperty> e) => ProductOnly(e.Entity.LocaleKeyGroup, e.Entity.EntityId);

    // Many products
    public Task HandleEventAsync(EntityUpdatedEvent<Category> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityDeletedEvent<Category> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<Manufacturer> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityDeletedEvent<Manufacturer> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<Discount> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityDeletedEvent<Discount> e) => store.QueueEverythingAsync();
    public Task HandleEventAsync(EntityUpdatedEvent<Currency> e) => store.QueueEverythingAsync();

    private Task ProductOnly(string entityName, int productId)
        => entityName == nameof(Product) ? store.QueueAsync(productId) : Task.CompletedTask;
}
