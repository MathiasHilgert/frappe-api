using Frappe.Geo.Contracts;

namespace Frappe.Geo.Infrastructure.Subdivisions;

/// <summary>A row of <c>geo.subdivisions</c>: an ISO 3166-2 subdivision.</summary>
internal sealed class SubdivisionRow
{
    public string SubdivisionCode { get; init; } = null!;

    public string CountryCode { get; init; } = null!;

    public ReferenceStatus Status { get; init; }
}
