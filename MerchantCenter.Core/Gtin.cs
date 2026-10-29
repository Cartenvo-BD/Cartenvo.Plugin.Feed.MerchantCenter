namespace Cartenvo.MerchantCenter.Core;

/// <summary>GTIN-8, UPC (12), EAN (13) and GTIN-14 validation.</summary>
public static class Gtin
{
    /// <summary>Removes spaces and dashes. Returns null for empty input.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var cleaned = new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }

    public static bool IsValid(string? value)
    {
        var gtin = Normalize(value);
        if (gtin is null || gtin.Length is not (8 or 12 or 13 or 14) || !gtin.All(char.IsAsciiDigit))
            return false;
        if (gtin.All(c => c == '0'))
            return false;

        // The check digit makes the weighted sum a multiple of 10. Weights alternate 3, 1 from the right,
        // starting with the digit next to the check digit.
        var sum = 0;
        for (var i = gtin.Length - 2; i >= 0; i--)
        {
            var digit = gtin[i] - '0';
            sum += (gtin.Length - 2 - i) % 2 == 0 ? digit * 3 : digit;
        }
        var check = (10 - sum % 10) % 10;
        return check == gtin[^1] - '0';
    }
}
