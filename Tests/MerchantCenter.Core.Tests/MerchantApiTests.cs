using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Cartenvo.MerchantCenter.Core;
using Cartenvo.MerchantCenter.Core.MerchantApi;
using NUnit.Framework;

namespace MerchantCenter.Core.Tests;

/// <summary>Answers requests with a function, and records them.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public class ServiceAccountKeyTests
{
    internal static string KeyJson(out RSA rsa, string tokenUri = "https://oauth2.googleapis.com/token")
    {
        rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        return new JsonObject
        {
            ["type"] = "service_account",
            ["project_id"] = "shop-123",
            ["private_key_id"] = "abc",
            ["private_key"] = pem,
            ["client_email"] = "feed@shop-123.iam.gserviceaccount.com",
            ["token_uri"] = tokenUri
        }.ToJsonString();
    }

    [Test]
    public void ReadsServiceAccountKey()
    {
        var key = ServiceAccountKey.TryParse(KeyJson(out _), out var error);
        Assert.That(error, Is.Null);
        Assert.That(key!.ClientEmail, Is.EqualTo("feed@shop-123.iam.gserviceaccount.com"));
        Assert.That(key.ProjectId, Is.EqualTo("shop-123"));
    }

    [TestCase("", "empty")]
    [TestCase("not json", "not_json")]
    [TestCase("{\"installed\":{\"client_id\":\"x\"}}", "not_service_account")]
    [TestCase("{\"type\":\"service_account\",\"client_email\":\"a@b\"}", "missing_fields")]
    public void RejectsOtherFiles(string json, string expected)
    {
        Assert.That(ServiceAccountKey.TryParse(json, out var error), Is.Null);
        Assert.That(error, Is.EqualTo(expected));
    }

    [Test]
    public void AssertionIsSignedWithTheKey()
    {
        var key = ServiceAccountKey.TryParse(KeyJson(out var rsa), out _)!;
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var jwt = GoogleTokenSource.CreateAssertion(key, now);
        var parts = jwt.Split('.');
        Assert.That(parts, Has.Length.EqualTo(3));

        var claims = JsonNode.Parse(Decode(parts[1]))!;
        Assert.That(claims["iss"]!.GetValue<string>(), Is.EqualTo(key.ClientEmail));
        Assert.That(claims["scope"]!.GetValue<string>(), Is.EqualTo(GoogleTokenSource.Scope));
        Assert.That(claims["aud"]!.GetValue<string>(), Is.EqualTo(key.TokenUri));
        Assert.That(claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>(), Is.EqualTo(3600));

        var signature = Convert.FromBase64String(Pad(parts[2].Replace('-', '+').Replace('_', '/')));
        Assert.That(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
    }

    [Test]
    public async Task TokensAreReusedUntilTheyExpire()
    {
        var key = ServiceAccountKey.TryParse(KeyJson(out _), out _)!;
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"t1\",\"expires_in\":3599}"));
        var now = DateTimeOffset.UtcNow;
        var source = new GoogleTokenSource(key, new HttpClient(handler), () => now);
        Assert.That(await source.GetTokenAsync(), Is.EqualTo("t1"));
        Assert.That(await source.GetTokenAsync(), Is.EqualTo("t1"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That(handler.Requests[0].Body, Does.Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer"));
        now = now.AddHours(1);
        await source.GetTokenAsync();
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void RefusedKeyIsReported()
    {
        var key = ServiceAccountKey.TryParse(KeyJson(out _), out _)!;
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"Invalid JWT Signature.\"}"));
        var source = new GoogleTokenSource(key, new HttpClient(handler));
        var ex = Assert.ThrowsAsync<GoogleAuthException>(() => source.GetTokenAsync());
        Assert.That(ex!.Code, Is.EqualTo("bad_key"));
        Assert.That(ex.Message, Is.EqualTo("Invalid JWT Signature."));
    }

    private static string Decode(string part) => Encoding.UTF8.GetString(Convert.FromBase64String(Pad(part.Replace('-', '+').Replace('_', '/'))));
    private static string Pad(string s) => s + new string('=', (4 - s.Length % 4) % 4);
}

public class ProductInputTests
{
    private static FeedItem Item() => new()
    {
        Id = "SKU-1", Title = "Blue shirt", Description = "Soft.", Link = "https://shop.test/shirt",
        ImageLink = "https://shop.test/1.jpg", Price = 55m, SalePrice = 43.5m, Currency = "usd", Brand = "Acme",
        Gtin = "4006381333931", Mpn = "BS-1", GoogleProductCategory = 212, ProductType = "Clothing > Shirts",
        ItemGroupId = "SKU", Color = "Blue", Size = "M", Gender = "male", AgeGroup = "adult",
        ShippingWeight = 0.25m, ShippingWeightUnit = "kg", Condition = "refurbished",
        SaleStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), SaleEndUtc = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc)
    };

    [Test]
    public void MapsEveryField()
    {
        var item = Item();
        item.AdditionalImageLinks.Add("https://shop.test/2.jpg");
        var input = ProductInputs.Build(item, "en", "US");
        Assert.That(input["offerId"]!.GetValue<string>(), Is.EqualTo("SKU-1"));
        Assert.That(input["contentLanguage"]!.GetValue<string>(), Is.EqualTo("en"));
        Assert.That(input["feedLabel"]!.GetValue<string>(), Is.EqualTo("US"));
        var a = input["productAttributes"]!;
        Assert.That(a["price"]!["amountMicros"]!.GetValue<string>(), Is.EqualTo("55000000"));
        Assert.That(a["price"]!["currencyCode"]!.GetValue<string>(), Is.EqualTo("USD"));
        Assert.That(a["salePrice"]!["amountMicros"]!.GetValue<string>(), Is.EqualTo("43500000"));
        Assert.That(a["salePriceEffectiveDate"]!["startTime"]!.GetValue<string>(), Is.EqualTo("2026-11-01T00:00:00Z"));
        Assert.That(a["salePriceEffectiveDate"]!["endTime"]!.GetValue<string>(), Is.EqualTo("2026-11-30T00:00:00Z"));
        Assert.That(a["availability"]!.GetValue<string>(), Is.EqualTo("IN_STOCK"));
        Assert.That(a["condition"]!.GetValue<string>(), Is.EqualTo("REFURBISHED"));
        Assert.That(a["gtins"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo("4006381333931"));
        Assert.That(a["googleProductCategory"]!.GetValue<string>(), Is.EqualTo("212"));
        Assert.That(a["productTypes"]!.AsArray().Single()!.GetValue<string>(), Is.EqualTo("Clothing > Shirts"));
        Assert.That(a["gender"]!.GetValue<string>(), Is.EqualTo("MALE"));
        Assert.That(a["ageGroup"]!.GetValue<string>(), Is.EqualTo("ADULT"));
        Assert.That(a["shippingWeight"]!["unit"]!.GetValue<string>(), Is.EqualTo("kg"));
        Assert.That(a["additionalImageLinks"]!.AsArray(), Has.Count.EqualTo(1));
    }

    [Test]
    public void SendsCustomLabelsAndLeavesOutEmptyOnes()
    {
        var item = new FeedItem { Id = "1", Title = "T", Price = 1 };
        item.CustomLabels[0] = "high-margin";
        item.CustomLabels[4] = "winter";
        var a = ProductInputs.Build(item, "en", "US")["productAttributes"]!;
        Assert.That(a["customLabel0"]!.GetValue<string>(), Is.EqualTo("high-margin"));
        Assert.That(a["customLabel4"]!.GetValue<string>(), Is.EqualTo("winter"));
        Assert.That(a.AsObject().ContainsKey("customLabel1"), Is.False);
    }

    [Test]
    public void SendsOnlyKnownFields()
    {
        var allowed = new HashSet<string>
        {
            "title", "description", "link", "imageLink", "additionalImageLinks", "availability", "availabilityDate", "price",
            "salePrice", "salePriceEffectiveDate", "brand", "gtins", "mpn", "identifierExists", "condition",
            "googleProductCategory", "productTypes", "itemGroupId", "color", "size", "gender", "ageGroup", "shippingWeight",
            "customLabel0", "customLabel1", "customLabel2", "customLabel3", "customLabel4"
        };
        var item = Item();
        for (var i = 0; i < FeedItem.CustomLabelCount; i++)
            item.CustomLabels[i] = "label " + i;
        item.IdentifierExists = false;
        item.Availability = ItemAvailability.Preorder;
        item.AvailabilityDateUtc = DateTime.UtcNow;
        var input = ProductInputs.Build(item, "en", "US");
        Assert.That(input.Select(p => p.Key), Is.EquivalentTo(new[] { "offerId", "contentLanguage", "feedLabel", "productAttributes" }));
        Assert.That(input["productAttributes"]!.AsObject().Select(p => p.Key), Is.SubsetOf(allowed));
        Assert.That(input["productAttributes"]!["identifierExists"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public void EmptyValuesAreLeftOut()
    {
        var input = ProductInputs.Build(new FeedItem { Id = "1", Title = "T", Price = 1 }, "de", "DE");
        var keys = input["productAttributes"]!.AsObject().Select(p => p.Key).ToList();
        Assert.That(keys, Is.EquivalentTo(new[] { "title", "availability", "price", "condition" }));
    }

    [Test]
    public void HashChangesOnlyWhenTheProductChanges()
    {
        var a = ProductInputs.Hash(ProductInputs.Build(Item(), "en", "US"));
        var b = ProductInputs.Hash(ProductInputs.Build(Item(), "en", "US"));
        var changed = Item();
        changed.Price = 56;
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Has.Length.EqualTo(64));
        Assert.That(ProductInputs.Hash(ProductInputs.Build(changed, "en", "US")), Is.Not.EqualTo(a));
    }

    [TestCase("en", "US", "sku123", "ZW5-VVN-c2t1MTIz")]
    [TestCase("en", "US", "sku/123", "ZW5-VVN-c2t1LzEyMw")] // Google's own example
    public void EncodesProductNames(string language, string label, string offerId, string expected)
        => Assert.That(ProductInputs.EncodedName(language, label, offerId), Is.EqualTo(expected));
}

public class MerchantApiClientTests
{
    private static MerchantApiClient Client(FakeHandler handler)
        => new(new HttpClient(handler), _ => Task.FromResult("token"), "123", "https://api.test", (_, _) => Task.CompletedTask);

    [Test]
    public async Task InsertsIntoTheDataSource()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, "{\"name\":\"accounts/123/productInputs/x\"}"));
        var result = await Client(handler).InsertProductAsync("accounts/123/dataSources/9", new JsonObject { ["offerId"] = "1" });
        Assert.That(result.Ok, Is.True);
        var (method, url, body) = handler.Requests.Single();
        Assert.That(method, Is.EqualTo(HttpMethod.Post));
        Assert.That(url, Is.EqualTo("https://api.test/products/v1/accounts/123/productInputs:insert?dataSource=accounts%2F123%2FdataSources%2F9"));
        Assert.That(body, Is.EqualTo("{\"offerId\":\"1\"}"));
    }

    [Test]
    public async Task RetriesWhenRateLimited()
    {
        var calls = 0;
        var handler = new FakeHandler((_, _) => ++calls < 3
            ? FakeHandler.Json(HttpStatusCode.TooManyRequests, "{\"error\":{\"code\":429,\"message\":\"quota/request_rate_too_high\",\"status\":\"RESOURCE_EXHAUSTED\"}}")
            : FakeHandler.Json(HttpStatusCode.OK, "{}"));
        var result = await Client(handler).InsertProductAsync("accounts/123/dataSources/9", new JsonObject());
        Assert.That(result.Ok, Is.True);
        Assert.That(calls, Is.EqualTo(3));
    }

    [Test]
    public async Task GivesUpAfterFiveAttempts()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"));
        var result = await Client(handler).GetAccountAsync();
        Assert.That(result.Ok, Is.False);
        Assert.That(handler.Requests, Has.Count.EqualTo(MerchantApiClient.MaxAttempts));
    }

    [Test]
    public async Task DeletingAMissingProductCountsAsDone()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":404,\"message\":\"not found\"}}"));
        var result = await Client(handler).DeleteProductAsync("accounts/123/dataSources/9", "en", "US", "sku/1");
        Assert.That(result.Ok, Is.True);
        Assert.That(handler.Requests.Single().Url, Does.StartWith("https://api.test/products/v1/accounts/123/productInputs/ZW5-VVN-c2t1LzE?dataSource="));
        Assert.That(handler.Requests.Single().Method, Is.EqualTo(HttpMethod.Delete));
    }

    [TestCase(401, "{\"error\":{\"code\":401,\"message\":\"GCP project with id 1 and number 2 is not registered with the merchant account.\",\"status\":\"UNAUTHENTICATED\"}}", ApiProblems.NotRegistered)]
    [TestCase(403, "{\"error\":{\"code\":403,\"message\":\"Merchant API has not been used in project 2 before or it is disabled.\",\"status\":\"PERMISSION_DENIED\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"SERVICE_DISABLED\"}]}}", ApiProblems.ApiDisabled)]
    [TestCase(403, "{\"error\":{\"code\":403,\"message\":\"The caller does not have access to the accounts: [123]\",\"status\":\"PERMISSION_DENIED\"}}", ApiProblems.NoAccess)]
    [TestCase(400, "{\"error\":{\"code\":400,\"message\":\"[attributes.price] Invalid value\",\"status\":\"INVALID_ARGUMENT\"}}", ApiProblems.Rejected)]
    public void ExplainsGoogleErrors(int status, string body, string expected)
    {
        var result = MerchantApiClient.Parse(status, body);
        Assert.That(result.Problem, Is.EqualTo(expected));
        Assert.That(result.Message, Is.Not.Empty);
    }

    [Test]
    public async Task KeyProblemsAreReportedNotThrown()
    {
        var client = new MerchantApiClient(new HttpClient(new FakeHandler((_, _) => throw new InvalidOperationException())),
            _ => throw new GoogleAuthException("bad_key", "Invalid JWT Signature."), "123");
        var result = await client.GetAccountAsync();
        Assert.That(result.Problem, Is.EqualTo(ApiProblems.BadKey));
    }
}
