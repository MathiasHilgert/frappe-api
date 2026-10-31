namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>A whole catalogue, in one page.</summary>
/// <typeparam name="TItem">The catalogue's item.</typeparam>
/// <param name="Items">The items, ordered by their id.</param>
/// <param name="NextCursor">Always <see langword="null"/>: the catalogue fits in one page.</param>
internal sealed record CataloguePage<TItem>(IReadOnlyList<TItem> Items, string? NextCursor);
