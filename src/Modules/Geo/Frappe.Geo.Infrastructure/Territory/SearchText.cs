using System.Globalization;
using System.Text;

namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>The form names are searched in: no diacritics, lower case, single spaces ("Córdoba" → "cordoba").</summary>
internal static class SearchText
{
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            kept.Append(char.ToLowerInvariant(c));
        }

        return string.Join(' ', kept.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
