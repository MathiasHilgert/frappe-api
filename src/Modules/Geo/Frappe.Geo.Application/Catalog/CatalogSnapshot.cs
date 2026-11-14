namespace Frappe.Geo.Application.Catalog;

/// <summary>Every country and currency the catalog holds, in the canonical shape every catalog seed produces.</summary>
public sealed record CatalogSnapshot(IReadOnlyList<CountryImport> Countries, IReadOnlyList<CurrencyImport> Currencies);
