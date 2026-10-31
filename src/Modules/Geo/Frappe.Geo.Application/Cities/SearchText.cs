using System.Globalization;
using System.Text;

namespace Frappe.Geo.Application.Cities;

/// <summary>
/// The one folding of city search text, shared by the seed and the query: accents dropped, lower case, whitespace
/// collapsed, so "Córdoba" and "cordoba" are the same text.
/// </summary>
public static class SearchText
{
    /// <summary>Folds a text for search.</summary>
    /// <param name="text">The text, such as a locality name or a search query.</param>
    /// <returns>The folded text, such as <c>villa maria</c> for <c>Villa  María</c>.</returns>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        StringBuilder folded = new(text.Length);
        bool space = false;
        foreach (char character in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsWhiteSpace(character))
            {
                space = folded.Length > 0;
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (space)
            {
                folded.Append(' ');
                space = false;
            }

            folded.Append(char.ToLowerInvariant(character));
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }
}
