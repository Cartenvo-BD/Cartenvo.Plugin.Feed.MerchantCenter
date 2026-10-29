using System.Text.Json.Nodes;

namespace Cartenvo.MerchantCenter.Core.MerchantApi;

/// <summary>What Google does with a product, worked out like Merchant Center's own summary. Stored as an int.</summary>
public enum GoogleStatus
{
    /// <summary>Google hasn't finished reviewing it.</summary>
    Pending = 0,
    /// <summary>Shown everywhere it was sent, with no issue that limits it.</summary>
    Approved = 1,
    /// <summary>Shown in some countries or places only, or shown less because of an issue.</summary>
    Limited = 2,
    /// <summary>Not shown anywhere.</summary>
    Disapproved = 3
}

/// <summary>One problem Google reports for a product, with its own explanation and help link.</summary>
public sealed class GoogleIssue
{
    public string Code { get; set; } = "";

    /// <summary>NOT_IMPACTED, DEMOTED or DISAPPROVED.</summary>
    public string Severity { get; set; } = "";

    /// <summary>MERCHANT_ACTION (the owner must fix it) or PENDING_PROCESSING (Google is still working on it).</summary>
    public string Resolution { get; set; } = "";
    public string? Attribute { get; set; }
    public string? Description { get; set; }
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public List<string> Countries { get; set; } = [];

    public bool Blocks => Severity == "DISAPPROVED";
    public bool Limits => Severity == "DEMOTED";
}

/// <summary>A product as Merchant Center shows it, from any data source.</summary>
public sealed class GoogleProductStatus
{
    public string OfferId { get; set; } = "";
    public string ContentLanguage { get; set; } = "";
    public string FeedLabel { get; set; } = "";
    public string? DataSource { get; set; }
    public GoogleStatus Status { get; set; }
    public SortedSet<string> ApprovedCountries { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> DisapprovedCountries { get; } = new(StringComparer.Ordinal);
    public SortedSet<string> PendingCountries { get; } = new(StringComparer.Ordinal);
    public List<GoogleIssue> Issues { get; } = [];
    public DateTime? ExpiresUtc { get; set; }
}

public static class GoogleStatuses
{
    /// <summary>Reads one entry of products.list. Null when it has no offer ID.</summary>
    public static GoogleProductStatus? Parse(JsonNode? product)
    {
        var offerId = Text(product?["offerId"]);
        if (product == null || string.IsNullOrEmpty(offerId))
            return null;
        var result = new GoogleProductStatus
        {
            OfferId = offerId,
            ContentLanguage = Text(product["contentLanguage"]) ?? "",
            FeedLabel = Text(product["feedLabel"]) ?? "",
            DataSource = Text(product["dataSource"])
        };
        var status = product["productStatus"];
        if (status?["destinationStatuses"] is JsonArray destinations)
            foreach (var destination in destinations)
            {
                AddAll(result.ApprovedCountries, destination?["approvedCountries"]);
                AddAll(result.DisapprovedCountries, destination?["disapprovedCountries"]);
                AddAll(result.PendingCountries, destination?["pendingCountries"]);
            }
        if (status?["itemLevelIssues"] is JsonArray issues)
            foreach (var issue in issues)
            {
                if (issue == null)
                    continue;
                var item = new GoogleIssue
                {
                    Code = Text(issue["code"]) ?? "",
                    Severity = Text(issue["severity"]) ?? "",
                    Resolution = Text(issue["resolution"]) ?? "",
                    Attribute = Text(issue["attribute"]),
                    Description = Text(issue["description"]),
                    Detail = Text(issue["detail"]),
                    Documentation = Text(issue["documentation"])
                };
                if (issue["applicableCountries"] is JsonArray countries)
                    item.Countries.AddRange(countries.Select(Text).Where(c => !string.IsNullOrEmpty(c)).Distinct()!);
                // The same issue is often listed once per reporting context (Shopping ads, free listings…).
                var same = result.Issues.FirstOrDefault(i => i.Code == item.Code && i.Attribute == item.Attribute);
                if (same == null)
                    result.Issues.Add(item);
                else
                {
                    if (Rank(item.Severity) > Rank(same.Severity))
                        same.Severity = item.Severity;
                    same.Countries = same.Countries.Union(item.Countries).ToList();
                }
            }
        if (DateTime.TryParse(Text(status?["googleExpirationDate"]), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var expires))
            result.ExpiresUtc = expires;
        result.Status = Summarise(result);
        return result;
    }

    /// <summary>
    /// Approved: shown everywhere it was sent with nothing limiting it. Limited: shown somewhere, but refused in a
    /// country or demoted by an issue. Disapproved: shown nowhere. Pending: still being reviewed.
    /// </summary>
    public static GoogleStatus Summarise(GoogleProductStatus product)
    {
        // A country can be approved for one destination and refused for another: approved wins for that country.
        var refused = product.DisapprovedCountries.Except(product.ApprovedCountries).Any();
        if (product.ApprovedCountries.Count > 0)
            return refused || product.Issues.Any(i => i.Blocks || i.Limits) ? GoogleStatus.Limited : GoogleStatus.Approved;
        if (product.DisapprovedCountries.Count > 0 || product.Issues.Any(i => i.Blocks && i.Resolution != "PENDING_PROCESSING"))
            return GoogleStatus.Disapproved;
        return GoogleStatus.Pending;
    }

    private static int Rank(string severity) => severity switch { "DISAPPROVED" => 3, "DEMOTED" => 2, "NOT_IMPACTED" => 1, _ => 0 };

    private static void AddAll(SortedSet<string> set, JsonNode? countries)
    {
        if (countries is JsonArray array)
            foreach (var country in array.Select(Text))
                if (!string.IsNullOrEmpty(country))
                    set.Add(country);
    }

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
