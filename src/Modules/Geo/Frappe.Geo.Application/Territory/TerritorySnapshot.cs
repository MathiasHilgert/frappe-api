namespace Frappe.Geo.Application.Territory;

/// <summary>
/// The canonical territory of one country, as every territory source reads it: first-level subdivisions, deeper
/// administrative areas and localities. Ids are the source's own, unique within the country.
/// </summary>
public sealed record TerritorySnapshot(
    string CountryCode,
    IReadOnlyList<SubdivisionImport> Subdivisions,
    IReadOnlyList<AdministrativeAreaImport> Areas,
    IReadOnlyList<LocalityImport> Localities);
