using System.Text;

namespace Cartenvo.MerchantCenter.Core;

public static class Text
{
    /// <summary>Removes characters XML can't hold and collapses runs of whitespace into one space.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            if (!IsXmlChar(c))
                continue;
            if (space)
            {
                builder.Append(' ');
                space = false;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    // Surrogate halves are kept; XmlWriter checks that pairs are complete.
    private static bool IsXmlChar(char c) => c >= 0x20 && c != 0xFFFE && c != 0xFFFF;

    /// <summary>Shortens text to <paramref name="max"/> characters, at a word break when there's one near the end.</summary>
    public static string Truncate(string value, int max, out bool truncated)
    {
        truncated = value.Length > max;
        if (!truncated)
            return value;
        var cut = value[..max];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > max * 0.8)
            cut = cut[..lastSpace];
        return cut.TrimEnd(' ', ',', ';', '-', '|', '/');
    }

    /// <summary>True when the text has at least 8 letters and none are lower case.</summary>
    public static bool IsAllCaps(string value)
    {
        var letters = value.Where(char.IsLetter).ToList();
        return letters.Count >= 8 && letters.All(c => !char.IsLower(c)) && letters.Any(char.IsUpper);
    }
}
