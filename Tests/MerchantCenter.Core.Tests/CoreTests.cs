using System.Xml.Linq;
using Cartenvo.MerchantCenter.Core;
using NUnit.Framework;

namespace MerchantCenter.Core.Tests;

public class GtinTests
{
    [TestCase("4006381333931")]   // EAN-13
    [TestCase("036000291452")]    // UPC-A
    [TestCase("96385074")]        // GTIN-8
    [TestCase("10036000291459")]  // GTIN-14
    [TestCase("4006 3813-33931")] // spaces and dashes are ignored
    public void ValidGtins(string gtin) => Assert.That(Gtin.IsValid(gtin), Is.True);

    [TestCase("4006381333932")] // wrong check digit
    [TestCase("12345")]
    [TestCase("ABCDEFGHIJKLM")]
    [TestCase("0000000000000")]
    [TestCase("")]
    [TestCase(null)]
    public void InvalidGtins(string? gtin) => Assert.That(Gtin.IsValid(gtin), Is.False);
}

public class FeedCheckTests
{
    private static FeedItem Good() => new()
    {
        Id = "SKU-1", Title = "Blue cotton shirt", Description = "A soft shirt.", Link = "https://shop.test/shirt",
        ImageLink = "https://shop.test/shirt.jpg", Price = 20, Currency = "USD", Brand = "Acme", Gtin = "4006381333931",
        GoogleProductCategory = 212
    };

    [Test]
    public void GoodItemHasNoIssues() => Assert.That(FeedChecks.Apply(Good()), Is.Empty);

    [Test]
    public void CustomLabelsAreCleanedAndCut()
    {
        var item = Good();
        item.CustomLabels[1] = "  ";
        item.CustomLabels[2] = new string('x', 150);
        Assert.That(FeedChecks.Apply(item), Is.Empty);
        Assert.That(item.CustomLabels[1], Is.Null);
        Assert.That(item.CustomLabels[2], Has.Length.EqualTo(FeedChecks.MaxCustomLabel));
    }

    [Test]
    public void MissingImageAndZeroPriceHoldItemBack()
    {
        var item = Good();
        item.ImageLink = " ";
        item.Price = 0;
        var issues = FeedChecks.Apply(item);
        Assert.That(issues.Select(i => i.Code), Is.EquivalentTo(new[] { IssueCodes.MissingImage, IssueCodes.ZeroPrice }));
        Assert.That(FeedChecks.IsHeldBack(issues), Is.True);
    }

    [Test]
    public void InvalidGtinIsRemovedButItemIsSent()
    {
        var item = Good();
        item.Gtin = "4006381333932";
        item.Mpn = "AC-1";
        var issues = FeedChecks.Apply(item);
        Assert.That(item.Gtin, Is.Null);
        Assert.That(issues.Select(i => i.Code), Is.EqualTo(new[] { IssueCodes.InvalidGtin }));
        Assert.That(FeedChecks.IsHeldBack(issues), Is.False);
    }

    [Test]
    public void LongTitleIsCutAtAWordBreak()
    {
        var item = Good();
        item.Title = string.Join(' ', Enumerable.Repeat("word", 60));
        var issues = FeedChecks.Apply(item);
        Assert.That(item.Title.Length, Is.LessThanOrEqualTo(FeedChecks.MaxTitle));
        Assert.That(item.Title, Does.EndWith("word"));
        Assert.That(issues.Select(i => i.Code), Does.Contain(IssueCodes.TitleTruncated));
    }

    [Test]
    public void SalePriceNotBelowPriceIsDropped()
    {
        var item = Good();
        item.SalePrice = 25;
        item.SaleEndUtc = DateTime.UtcNow;
        FeedChecks.Apply(item);
        Assert.That(item.SalePrice, Is.Null);
        Assert.That(item.SaleEndUtc, Is.Null);
    }

    [Test]
    public void NoIdentifiersWarnsUnlessCustomMade()
    {
        var item = Good();
        item.Gtin = null;
        Assert.That(FeedChecks.Apply(item).Select(i => i.Code), Does.Contain(IssueCodes.MissingIdentifiers));

        var custom = Good();
        custom.Gtin = null;
        Assert.That(FeedChecks.Apply(custom, noIdentifiersMeansCustom: true), Is.Empty);
        Assert.That(custom.IdentifierExists, Is.False);
    }

    [Test]
    public void BrandAndMpnCountAsIdentifiers()
    {
        var item = Good();
        item.Gtin = null;
        item.Mpn = "AC-1";
        Assert.That(FeedChecks.Apply(item), Is.Empty);
    }

    [Test]
    public void TextIsCleanedOfControlCharactersAndExtraSpaces()
    {
        var item = Good();
        item.Title = "  Blue\u0001   shirt \n ";
        FeedChecks.Apply(item);
        Assert.That(item.Title, Is.EqualTo("Blue shirt"));
    }

    [Test]
    public void AllCapsTitleAndHttpLinkWarn()
    {
        var item = Good();
        item.Title = "BLUE COTTON SHIRT";
        item.Link = "http://shop.test/shirt";
        var codes = FeedChecks.Apply(item).Select(i => i.Code).ToList();
        Assert.That(codes, Is.EquivalentTo(new[] { IssueCodes.TitleAllCaps, IssueCodes.NotHttps }));
    }
}

public class FeedXmlWriterTests
{
    private static readonly XNamespace G = FeedXmlWriter.GoogleNamespace;

    [Test]
    public void WritesGoogleRssItems()
    {
        using var stream = new MemoryStream();
        using (var writer = new FeedXmlWriter(stream, "Shop", "https://shop.test/", "All products"))
        {
            var item = new FeedItem
            {
                Id = "SKU-1", Title = "Shirt & tie", Description = "Nice", Link = "https://shop.test/shirt",
                ImageLink = "https://shop.test/1.jpg", Price = 20.5m, SalePrice = 15m, Currency = "eur",
                SaleStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), Brand = "Acme",
                IdentifierExists = false, GoogleProductCategory = 212, ShippingWeight = 1.25m, ShippingWeightUnit = "kg",
                Availability = ItemAvailability.OutOfStock
            };
            item.AdditionalImageLinks.Add("https://shop.test/2.jpg");
            item.CustomLabels[3] = "bestseller";
            writer.Write(item);
            Assert.That(writer.ItemCount, Is.EqualTo(1));
        }
        stream.Position = 0;
        var doc = XDocument.Load(stream);
        var element = doc.Root!.Element("channel")!.Element("item")!;
        Assert.That(element.Element(G + "title")!.Value, Is.EqualTo("Shirt & tie"));
        Assert.That(element.Element(G + "price")!.Value, Is.EqualTo("20.50 EUR"));
        Assert.That(element.Element(G + "sale_price")!.Value, Is.EqualTo("15.00 EUR"));
        Assert.That(element.Element(G + "sale_price_effective_date")!.Value, Is.EqualTo("2026-11-01T00:00Z/2027-11-01T00:00Z"));
        Assert.That(element.Element(G + "availability")!.Value, Is.EqualTo("out_of_stock"));
        Assert.That(element.Element(G + "identifier_exists")!.Value, Is.EqualTo("no"));
        Assert.That(element.Element(G + "google_product_category")!.Value, Is.EqualTo("212"));
        Assert.That(element.Element(G + "shipping_weight")!.Value, Is.EqualTo("1.25 kg"));
        Assert.That(element.Elements(G + "additional_image_link").Count(), Is.EqualTo(1));
        Assert.That(element.Element(G + "custom_label_3")!.Value, Is.EqualTo("bestseller"));
        Assert.That(element.Element(G + "custom_label_0"), Is.Null);
        Assert.That(element.Element(G + "gtin"), Is.Null, "empty values are left out");
    }

    [Test]
    public void EmptyFeedIsValidXml()
    {
        using var stream = new MemoryStream();
        new FeedXmlWriter(stream, "Shop", "https://shop.test/", "None").Dispose();
        stream.Position = 0;
        Assert.That(XDocument.Load(stream).Root!.Element("channel")!.Elements("item"), Is.Empty);
    }
}

public class OfferIdTests
{
    [Test]
    public void SkuFallsBackToProductId()
    {
        Assert.That(OfferIds.ForProduct(OfferIdSource.SkuThenProductId, 7, " A1 "), Is.EqualTo("A1"));
        Assert.That(OfferIds.ForProduct(OfferIdSource.SkuThenProductId, 7, ""), Is.EqualTo("7"));
        Assert.That(OfferIds.ForProduct(OfferIdSource.ProductId, 7, "A1"), Is.EqualTo("7"));
        Assert.That(OfferIds.ForProduct(OfferIdSource.Sku, 7, null), Is.Null);
    }

    [Test]
    public void CombinationUsesItsSkuOrTheProductIdWithCombinationId()
    {
        Assert.That(OfferIds.ForCombination(OfferIdSource.SkuThenProductId, "A1", 3, "A1-RED"), Is.EqualTo("A1-RED"));
        Assert.That(OfferIds.ForCombination(OfferIdSource.SkuThenProductId, "A1", 3, null), Is.EqualTo("A1-3"));
        Assert.That(OfferIds.ForCombination(OfferIdSource.ProductId, "7", 3, "A1-RED"), Is.EqualTo("7-3"));
    }

    [Test]
    public void MerchantApiNameIsEncodedOnlyWhenNeeded()
    {
        Assert.That(OfferIds.MerchantApiName("en", "US", "SKU1"), Is.EqualTo("en~US~SKU1"));
        var encoded = OfferIds.MerchantApiName("en", "US", "A/B");
        Assert.That(encoded, Does.Not.Contain("/").And.Not.Contain("="));
        var padded = encoded.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        Assert.That(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Is.EqualTo("en~US~A/B"));
    }
}

public class TaxonomyTests
{
    private const string Sample = """
        # Google_Product_Taxonomy_Version: 2021-09-21
        1 - Animals & Pet Supplies
        166 - Apparel & Accessories
        1604 - Apparel & Accessories > Clothing
        212 - Apparel & Accessories > Clothing > Shirts & Tops
        187 - Apparel & Accessories > Shoes
        222 - Electronics
        278 - Electronics > Computers
        328 - Electronics > Computers > Laptops
        325 - Electronics > Computers > Desktop Computers
        267 - Electronics > Communications > Telephony > Mobile Phones
        543514 - Electronics > Communications > Telephony > Mobile Phones > Unlocked Mobile Phones
        """;

    private static TaxonomyIndex Index() => TaxonomyIndex.Parse(new StringReader(Sample));

    [Test]
    public void ParsesIdsAndPaths()
    {
        var index = Index();
        Assert.That(index.Count, Is.EqualTo(11));
        Assert.That(index.Get(328)!.Name, Is.EqualTo("Laptops"));
        Assert.That(index.Get(328)!.Depth, Is.EqualTo(3));
    }

    [Test]
    public void SearchFindsByWordsAndById()
    {
        var index = Index();
        Assert.That(index.Search("laptop").Select(c => c.Id), Is.EqualTo(new[] { 328 }));
        Assert.That(index.Search("mobile phones").First().Id, Is.EqualTo(267));
        Assert.That(index.Search("187").Single().Name, Is.EqualTo("Shoes"));
        Assert.That(index.Search(""), Is.Empty);
    }

    [TestCase("Notebooks & Laptops", 328)]
    [TestCase("Laptops", 328)]
    [TestCase("Shoes", 187)]
    [TestCase("Shirts", 212)]
    [TestCase("Desktops", 325)]
    public void SuggestsAMatchingCategory(string name, int expected)
        => Assert.That(Index().Suggest(name)?.Category.Id, Is.EqualTo(expected));

    [Test]
    public void ParentNamesBreakTies()
        => Assert.That(Index().Suggest("Mobile phones", ["Electronics"])?.Category.Id, Is.EqualTo(267));

    [Test]
    public void NoSuggestionWithoutAMatch() => Assert.That(Index().Suggest("Gift cards"), Is.Null);
}
