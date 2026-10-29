using System.Globalization;
using System.Text;
using System.Xml;

namespace Cartenvo.MerchantCenter.Core;

/// <summary>Writes a Google product feed: RSS 2.0 with the g: namespace.</summary>
public sealed class FeedXmlWriter : IDisposable
{
    public const string GoogleNamespace = "http://base.google.com/ns/1.0";
    private readonly XmlWriter _writer;
    private bool _completed;

    public FeedXmlWriter(Stream stream, string title, string link, string description, bool indent = true)
    {
        _writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = indent,
            CloseOutput = false
        });
        _writer.WriteStartDocument();
        _writer.WriteStartElement("rss");
        _writer.WriteAttributeString("version", "2.0");
        _writer.WriteAttributeString("xmlns", "g", null, GoogleNamespace);
        _writer.WriteStartElement("channel");
        _writer.WriteElementString("title", Text.Clean(title));
        _writer.WriteElementString("link", link);
        _writer.WriteElementString("description", Text.Clean(description));
    }

    public int ItemCount { get; private set; }

    public void Write(FeedItem item)
    {
        _writer.WriteStartElement("item");
        G("id", item.Id);
        G("title", item.Title);
        G("description", item.Description);
        G("link", item.Link);
        G("image_link", item.ImageLink);
        foreach (var image in item.AdditionalImageLinks)
            G("additional_image_link", image);
        G("availability", AvailabilityValue(item.Availability));
        if (item.AvailabilityDateUtc is { } availableOn && item.Availability is ItemAvailability.Preorder or ItemAvailability.Backorder)
            G("availability_date", Date(availableOn));
        G("price", Price(item.Price, item.Currency));
        if (item.SalePrice is { } sale)
        {
            G("sale_price", Price(sale, item.Currency));
            if (item.SaleStartUtc is not null || item.SaleEndUtc is not null)
                G("sale_price_effective_date", SaleDates(item.SaleStartUtc, item.SaleEndUtc));
        }
        G("brand", item.Brand);
        G("gtin", item.Gtin);
        G("mpn", item.Mpn);
        if (item.IdentifierExists == false)
            G("identifier_exists", "no");
        G("condition", item.Condition);
        if (item.GoogleProductCategory is { } category)
            G("google_product_category", category.ToString(CultureInfo.InvariantCulture));
        G("product_type", item.ProductType);
        G("item_group_id", item.ItemGroupId);
        G("color", item.Color);
        G("size", item.Size);
        G("gender", item.Gender);
        G("age_group", item.AgeGroup);
        if (item.ShippingWeight is > 0 && item.ShippingWeightUnit is not null)
            G("shipping_weight", $"{item.ShippingWeight.Value.ToString("0.###", CultureInfo.InvariantCulture)} {item.ShippingWeightUnit}");
        for (var i = 0; i < FeedItem.CustomLabelCount; i++)
            G("custom_label_" + i.ToString(CultureInfo.InvariantCulture), item.CustomLabels[i]);
        _writer.WriteEndElement();
        ItemCount++;
    }

    public void Complete()
    {
        if (_completed)
            return;
        _writer.WriteEndElement(); // channel
        _writer.WriteEndElement(); // rss
        _writer.WriteEndDocument();
        _writer.Flush();
        _completed = true;
    }

    public void Dispose()
    {
        Complete();
        _writer.Dispose();
    }

    private void G(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _writer.WriteElementString("g", name, GoogleNamespace, value);
    }

    public static string Price(decimal amount, string currency)
        => $"{Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)} {currency.ToUpperInvariant()}";

    public static string AvailabilityValue(ItemAvailability availability) => availability switch
    {
        ItemAvailability.OutOfStock => "out_of_stock",
        ItemAvailability.Preorder => "preorder",
        ItemAvailability.Backorder => "backorder",
        _ => "in_stock"
    };

    public static string Date(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture);

    /// <summary>ISO 8601 interval. An open start uses today; an open end uses one year after the start.</summary>
    public static string SaleDates(DateTime? startUtc, DateTime? endUtc)
    {
        var start = startUtc ?? DateTime.UtcNow.Date;
        var end = endUtc ?? start.AddYears(1);
        return $"{Date(start)}/{Date(end)}";
    }
}
