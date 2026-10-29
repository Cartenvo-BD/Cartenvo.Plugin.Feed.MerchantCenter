using System.Globalization;
using System.Text;

namespace Cartenvo.MerchantCenter.Core;

public sealed record TaxonomyCategory(int Id, string Path)
{
    public string Name => Path[(Path.LastIndexOf('>') + 1)..].Trim();
    public int Depth => Path.Count(c => c == '>') + 1;
}

public sealed record TaxonomySuggestion(TaxonomyCategory Category, double Score);

/// <summary>
/// Google's product taxonomy (the "taxonomy-with-ids" file: "<c>166 - Apparel &amp; Accessories</c>"), with search and
/// name-based suggestions for mapping store categories.
/// </summary>
public sealed class TaxonomyIndex
{
    private readonly Dictionary<int, TaxonomyCategory> _byId;
    private readonly List<(TaxonomyCategory Category, string[] NameTokens, HashSet<string> PathTokens, string Lower)> _entries;

    public TaxonomyIndex(IEnumerable<TaxonomyCategory> categories)
    {
        var list = categories.ToList();
        _byId = list.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        _entries = list.Select(c => (c, Tokens(c.Name), new HashSet<string>(Tokens(c.Path)), c.Path.ToLowerInvariant())).ToList();
    }

    public int Count => _byId.Count;

    public static TaxonomyIndex Parse(TextReader reader)
    {
        var categories = new List<TaxonomyCategory>();
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#'))
                continue;
            var dash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (dash <= 0 || !int.TryParse(line.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                continue;
            var path = line[(dash + 3)..].Trim();
            if (path.Length > 0)
                categories.Add(new TaxonomyCategory(id, path));
        }
        return new TaxonomyIndex(categories);
    }

    public TaxonomyCategory? Get(int id) => _byId.GetValueOrDefault(id);

    /// <summary>Categories whose path contains every word of the query, best matches first. A number finds that ID.</summary>
    public IReadOnlyList<TaxonomyCategory> Search(string? query, int max = 20)
    {
        query = query?.Trim() ?? "";
        if (query.Length == 0)
            return [];
        if (int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return Get(id) is { } exact ? [exact] : [];

        var words = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _entries
            .Where(entry => words.All(word => entry.Lower.Contains(word, StringComparison.Ordinal)))
            .Select(entry => (entry.Category, Rank: Rank(entry.Category, words)))
            .OrderByDescending(match => match.Rank)
            .ThenBy(match => match.Category.Depth)
            .ThenBy(match => match.Category.Path, StringComparer.Ordinal)
            .Take(max)
            .Select(match => match.Category)
            .ToList();
    }

    private static int Rank(TaxonomyCategory category, string[] words)
    {
        var name = category.Name.ToLowerInvariant();
        var rank = 0;
        if (words.Length == 1 && name == words[0])
            rank += 100;
        rank += words.Count(word => name.Contains(word, StringComparison.Ordinal)) * 10;
        return rank;
    }

    /// <summary>
    /// The best Google category for a store category, from its name and the names of its parents. Null when nothing
    /// matches well enough to suggest.
    /// </summary>
    public TaxonomySuggestion? Suggest(string categoryName, IEnumerable<string>? parentNames = null)
    {
        var parentTokens = new HashSet<string>((parentNames ?? []).SelectMany(Tokens));
        var nameTokens = Synonyms(Tokens(categoryName), parentTokens);
        if (nameTokens.Length == 0)
            return null;
        var nameSet = new HashSet<string>(nameTokens);

        TaxonomySuggestion? best = null;
        foreach (var (category, leafTokens, pathTokens, _) in _entries)
        {
            var leafHits = leafTokens.Count(nameSet.Contains);
            if (leafHits == 0)
                continue;
            // Share of the store category name found in Google's leaf name, and of Google's leaf name in ours.
            var coverage = (double)leafHits / nameTokens.Length;
            var precision = (double)leafHits / leafTokens.Length;
            var score = (coverage + precision) / 2;
            if (leafTokens.SequenceEqual(nameTokens))
                score += 0.3;
            var context = parentTokens.Count(pathTokens.Contains);
            score += Math.Min(context, 3) * 0.08;
            score -= category.Depth * 0.01;
            if (best is null || score > best.Score)
                best = new TaxonomySuggestion(category, Math.Round(score, 3));
        }
        return best is { Score: >= 0.6 } ? best : null;
    }

    private static readonly HashSet<string> StopWords = ["and", "the", "for", "of", "with", "other", "misc"];

    private static readonly HashSet<string> ComputerWords = ["computer", "electronic", "laptop", "pc", "it"];

    /// <summary>Common store words that Google names differently.</summary>
    private static string[] Synonyms(string[] tokens, HashSet<string> parentTokens)
    {
        var result = new List<string>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case "cell" or "cellphone" or "smartphone":
                    result.Add("mobile");
                    if (token != "cell")
                        result.Add("phone");
                    break;
                case "tv" or "tvs":
                    result.Add("television");
                    break;
                case "tablet":
                    result.Add("tablet");
                    result.Add("computer");
                    break;
                case "notebook" when parentTokens.Overlaps(ComputerWords):
                    result.Add("laptop");
                    break;
                default:
                    result.Add(token);
                    break;
            }
        }
        return result.Distinct().ToArray();
    }

    /// <summary>Lower-case words, with simple plurals made singular, so "Laptops" matches "Laptop".</summary>
    public static string[] Tokens(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c))
                current.Append(c);
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                Flush();
        }
        Flush();
        return words.ToArray();

        void Flush()
        {
            if (current.Length == 0)
                return;
            var word = Singular(current.ToString());
            current.Clear();
            if (word.Length > 1 && !StopWords.Contains(word))
                words.Add(word);
        }
    }

    private static string Singular(string word)
    {
        if (word.Length > 4 && word.EndsWith("ies", StringComparison.Ordinal))
            return word[..^3] + "y";
        if (word.Length > 4 && (word.EndsWith("ches", StringComparison.Ordinal) || word.EndsWith("shes", StringComparison.Ordinal)
            || word.EndsWith("sses", StringComparison.Ordinal) || word.EndsWith("xes", StringComparison.Ordinal)))
            return word[..^2];
        if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal))
            return word[..^1];
        return word;
    }
}
