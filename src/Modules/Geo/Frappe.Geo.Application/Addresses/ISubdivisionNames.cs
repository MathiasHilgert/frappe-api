namespace Frappe.Geo.Application.Addresses;

/// <summary>
/// Outbound port: maps the names a provider gives a first-level subdivision to its ISO 3166-2 code, through the names and
/// aliases Geo keeps (source and manual, S3), compared in their search form (case and accents ignored).
/// </summary>
public interface ISubdivisionNames
{
    /// <summary>The code of the first of <paramref name="names"/> that names a current subdivision of <paramref name="countryCode"/>; <see langword="null"/> when none does.</summary>
    Task<string?> FindCodeAsync(string countryCode, IReadOnlyList<string> names, CancellationToken cancellationToken);
}
