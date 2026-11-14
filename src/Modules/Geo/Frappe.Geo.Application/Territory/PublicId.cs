using System.Diagnostics.CodeAnalysis;

namespace Frappe.Geo.Application.Territory;

/// <summary>
/// The public id of an area or a locality: the country's alpha-2 code and the source's id, <c>AR:30015</c>. Source ids
/// are unique only within a country, so the country is part of the id.
/// </summary>
public static class PublicId
{
    public static string Of(string countryCode, string sourceId) => countryCode + ":" + sourceId;

    public static bool TryParse(string? value, [NotNullWhen(true)] out string? countryCode, [NotNullWhen(true)] out string? sourceId)
    {
        countryCode = null;
        sourceId = null;
        if (value is null || value.Length < 4 || value[2] != ':' || !value[..2].All(char.IsAsciiLetter) || !value[3..].All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        countryCode = value[..2].ToUpperInvariant();
        sourceId = value[3..];
        return true;
    }
}
