using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Infrastructure.Cities;

/// <summary>A row of <c>geo.cities</c>: a Georef locality with its folded search columns.</summary>
internal sealed class CityRow
{
    public Guid CityId { get; init; }

    public string IndecId { get; init; } = null!;

    public string CountryCode { get; init; } = null!;

    public string SubdivisionCode { get; init; } = null!;

    public string Name { get; init; } = null!;

    public string? DepartmentName { get; init; }

    public string SearchName { get; init; } = null!;

    public string SearchText { get; init; } = null!;

    public TimeZoneId TimeZoneId { get; init; } = null!;

    public ReferenceStatus Status { get; init; }
}
