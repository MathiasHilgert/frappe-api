namespace Frappe.Geo.Infrastructure.Territory;

/// <summary>A row of <c>geo.subdivision_aliases</c>: another name a subdivision is known by ("CABA"), and its search form.</summary>
internal sealed class SubdivisionAliasRow
{
    /// <summary>
    /// The unique index on (subdivision, search form): a subdivision is known by an alias once, ignoring case and accents.
    /// A manual alias wins over the source's of the same search form (<see cref="SubdivisionRow.CopyFrom"/>).
    /// </summary>
    public const string OneBySearchForm = "ux_subdivision_aliases_subdivision_search_form";

    /// <summary>
    /// The partial unique index on (country, search form) of the manual aliases: one subdivision of a country is known
    /// by a manual alias, so two administrators adding it to two subdivisions at once cannot both succeed. The source's
    /// aliases are not in it, so the territory sync never fails on it. Raw SQL in migration
    /// <c>AddManualAliasCountryUniqueness</c>: the model cannot express an index on part of the code.
    /// </summary>
    public const string OneManualByCountry = "ux_subdivision_aliases_manual_country_search_form";

    public required string Alias { get; init; }

    public required string SearchForm { get; set; }

    /// <summary>Added by an administrator: the territory sync never removes or replaces it.</summary>
    public bool Manual { get; init; }
}
