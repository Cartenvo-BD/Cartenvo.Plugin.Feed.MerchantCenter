using System.Text.Json.Nodes;
using Cartenvo.MerchantCenter.Core.MerchantApi;
using NUnit.Framework;

namespace MerchantCenter.Core.Tests;

public class GoogleStatusTests
{
    private static GoogleProductStatus Parse(string json) => GoogleStatuses.Parse(JsonNode.Parse(json))!;

    private const string Disapproved = """
        {
          "name": "accounts/1/products/en~US~SKU-1", "offerId": "SKU-1", "contentLanguage": "en", "feedLabel": "US",
          "dataSource": "accounts/1/dataSources/99",
          "productStatus": {
            "destinationStatuses": [
              { "reportingContext": "SHOPPING_ADS", "disapprovedCountries": ["US"] },
              { "reportingContext": "FREE_LISTINGS", "disapprovedCountries": ["US"] }
            ],
            "itemLevelIssues": [
              { "code": "image_link_broken", "severity": "DISAPPROVED", "resolution": "MERCHANT_ACTION", "attribute": "image link",
                "reportingContext": "SHOPPING_ADS", "description": "Invalid image [image link]", "detail": "Ensure the image is accessible",
                "documentation": "https://support.google.com/merchants/answer/6098289", "applicableCountries": ["US"] },
              { "code": "image_link_broken", "severity": "DISAPPROVED", "resolution": "MERCHANT_ACTION", "attribute": "image link",
                "reportingContext": "FREE_LISTINGS", "applicableCountries": ["US"] }
            ],
            "googleExpirationDate": "2026-11-28T10:00:00Z"
          }
        }
        """;

    [Test]
    public void ReadsIssuesOnceWithGooglesTexts()
    {
        var product = Parse(Disapproved);
        Assert.That(product.OfferId, Is.EqualTo("SKU-1"));
        Assert.That(product.DataSource, Is.EqualTo("accounts/1/dataSources/99"));
        Assert.That(product.Status, Is.EqualTo(GoogleStatus.Disapproved));
        Assert.That(product.Issues, Has.Count.EqualTo(1), "one issue listed for two reporting contexts");
        var issue = product.Issues[0];
        Assert.That(issue.Description, Is.EqualTo("Invalid image [image link]"));
        Assert.That(issue.Documentation, Does.StartWith("https://support.google.com/"));
        Assert.That(issue.Countries, Is.EqualTo(new[] { "US" }));
        Assert.That(product.ExpiresUtc, Is.EqualTo(new DateTime(2026, 11, 28, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ApprovedWithNoIssues()
        => Assert.That(Parse("""
            {"offerId":"1","productStatus":{"destinationStatuses":[{"approvedCountries":["US","CA"]}]}}
            """).Status, Is.EqualTo(GoogleStatus.Approved));

    [Test]
    public void LimitedWhenRefusedInOneCountry()
        => Assert.That(Parse("""
            {"offerId":"1","productStatus":{"destinationStatuses":[{"approvedCountries":["US"],"disapprovedCountries":["DE"]}]}}
            """).Status, Is.EqualTo(GoogleStatus.Limited));

    [Test]
    public void ApprovedWhenOneDestinationCoversTheCountry()
        => Assert.That(Parse("""
            {"offerId":"1","productStatus":{"destinationStatuses":[
              {"reportingContext":"SHOPPING_ADS","disapprovedCountries":["US"]},
              {"reportingContext":"FREE_LISTINGS","approvedCountries":["US"]}]}}
            """).Status, Is.EqualTo(GoogleStatus.Approved));

    [Test]
    public void LimitedWhenDemoted()
        => Assert.That(Parse("""
            {"offerId":"1","productStatus":{"destinationStatuses":[{"approvedCountries":["US"]}],
             "itemLevelIssues":[{"code":"missing_gtin","severity":"DEMOTED","resolution":"MERCHANT_ACTION"}]}}
            """).Status, Is.EqualTo(GoogleStatus.Limited));

    [Test]
    public void ApprovedDespiteMinorIssue()
        => Assert.That(Parse("""
            {"offerId":"1","productStatus":{"destinationStatuses":[{"approvedCountries":["US"]}],
             "itemLevelIssues":[{"code":"image_too_small","severity":"NOT_IMPACTED"}]}}
            """).Status, Is.EqualTo(GoogleStatus.Approved));

    [Test]
    public void PendingWhileGoogleReviews()
    {
        Assert.That(Parse("""{"offerId":"1","productStatus":{"destinationStatuses":[{"pendingCountries":["US"]}]}}""").Status,
            Is.EqualTo(GoogleStatus.Pending));
        Assert.That(Parse("""{"offerId":"1"}""").Status, Is.EqualTo(GoogleStatus.Pending));
        Assert.That(Parse("""
            {"offerId":"1","productStatus":{"itemLevelIssues":[{"code":"pending_crawl","severity":"DISAPPROVED","resolution":"PENDING_PROCESSING"}]}}
            """).Status, Is.EqualTo(GoogleStatus.Pending));
    }

    [Test]
    public void SkipsProductsWithoutOfferId()
        => Assert.That(GoogleStatuses.Parse(JsonNode.Parse("""{"name":"x"}""")), Is.Null);
}
