namespace Frappe.Geo.Application.Territory;

/// <summary>
/// What a manual alias may be written with: letters (accents included), digits, spaces and the punctuation place names
/// use (<c>. , ' ’ - ( ) &amp;</c>), with at least one letter or digit, up to <see cref="MaxLength"/> characters. Every
/// alias is a single route segment (<c>…/aliases/{alias}</c>), so <c>/</c>, <c>\</c> and <c>%</c> (which a route
/// cannot carry back unchanged) and dot-only names (which a path normalises away) can never be added or removed.
/// </summary>
public static class SubdivisionAliasText
{
    public const int MaxLength = 200;

    /// <summary>The alias trimmed, or null when it breaks the rule.</summary>
    public static string? Validate(string? alias)
    {
        var trimmed = alias?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= MaxLength && trimmed.All(Allowed) && trimmed.Any(char.IsLetterOrDigit) ? trimmed : null;
    }

    private static bool Allowed(char c) =>
        char.IsLetterOrDigit(c) || c == ' ' || ".,'’-()&".Contains(c, StringComparison.Ordinal)
        || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark;
}
