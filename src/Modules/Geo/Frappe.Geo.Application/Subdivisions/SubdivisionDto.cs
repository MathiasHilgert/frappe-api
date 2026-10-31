using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Application.Subdivisions;

/// <summary>An ISO 3166-2 subdivision, such as an Argentine province.</summary>
/// <param name="SubdivisionCode">The ISO 3166-2 code, such as AR-X.</param>
/// <param name="Name">The name in the requested locale, or the nearest one along its fallback chain.</param>
public sealed record SubdivisionDto(SubdivisionCode SubdivisionCode, string Name);
