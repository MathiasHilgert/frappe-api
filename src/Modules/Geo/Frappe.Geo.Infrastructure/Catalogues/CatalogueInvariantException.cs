namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>A catalogue read broke an invariant: a bug that no stale snapshot may hide.</summary>
internal sealed class CatalogueInvariantException(string message) : Exception(message);
