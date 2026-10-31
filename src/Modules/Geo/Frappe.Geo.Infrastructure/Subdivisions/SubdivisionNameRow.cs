namespace Frappe.Geo.Infrastructure.Subdivisions;

/// <summary>A row of <c>geo.subdivision_names</c>: a subdivision's name in one locale.</summary>
internal sealed class SubdivisionNameRow
{
    public string SubdivisionCode { get; init; } = null!;

    public string Locale { get; init; } = null!;

    public string Name { get; init; } = null!;
}
