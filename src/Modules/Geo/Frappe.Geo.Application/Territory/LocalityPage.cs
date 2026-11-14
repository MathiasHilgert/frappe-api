namespace Frappe.Geo.Application.Territory;

/// <summary>One page of localities and the cursor of the next one (none on the last page).</summary>
public sealed record LocalityPage(IReadOnlyList<LocalityView> Localities, string? NextCursor);
