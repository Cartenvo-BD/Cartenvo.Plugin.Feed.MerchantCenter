namespace Cartenvo.MerchantCenter.Core;

public enum ItemAvailability
{
    InStock,
    OutOfStock,
    Preorder,
    Backorder
}

/// <summary>One product (or one attribute combination) as Google Merchant Center sees it.</summary>
public sealed class FeedItem
{
    /// <summary>nopCommerce product the item comes from. Not sent to Google.</summary>
    public int ProductId { get; set; }

    /// <summary>Attribute combination the item comes from, when variants are sent separately. Not sent to Google.</summary>
    public int? CombinationId { get; set; }

    public string Id { get; set; } = "";
    public string? ItemGroupId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Link { get; set; } = "";
    public string? ImageLink { get; set; }
    public List<string> AdditionalImageLinks { get; } = [];

    public ItemAvailability Availability { get; set; } = ItemAvailability.InStock;
    public DateTime? AvailabilityDateUtc { get; set; }

    /// <summary>Regular price, in <see cref="Currency"/>.</summary>
    public decimal Price { get; set; }

    /// <summary>Lower price customers pay now, or null when there's no sale.</summary>
    public decimal? SalePrice { get; set; }
    public DateTime? SaleStartUtc { get; set; }
    public DateTime? SaleEndUtc { get; set; }
    public string Currency { get; set; } = "USD";

    public string? Brand { get; set; }
    public string? Gtin { get; set; }
    public string? Mpn { get; set; }

    /// <summary>False sends identifier_exists=no. Null leaves it out.</summary>
    public bool? IdentifierExists { get; set; }

    public string Condition { get; set; } = "new";
    public int? GoogleProductCategory { get; set; }
    public string? ProductType { get; set; }

    public string? Color { get; set; }
    public string? Size { get; set; }
    public string? Gender { get; set; }
    public string? AgeGroup { get; set; }

    public decimal? ShippingWeight { get; set; }

    /// <summary>Google unit: lb, oz, g or kg.</summary>
    public string? ShippingWeightUnit { get; set; }

    /// <summary>custom_label_0 to custom_label_4: free text for grouping products in Google Ads. Empty ones are left out.</summary>
    public string?[] CustomLabels { get; } = new string?[CustomLabelCount];

    public const int CustomLabelCount = 5;

    /// <summary>The price a shopper sees, which is the sale price when there is one.</summary>
    public decimal EffectivePrice => SalePrice ?? Price;
}
