using Frappe.Geo.Contracts;

namespace Frappe.Geo.Infrastructure.Countries;

/// <summary>A row of <c>geo.countries</c>: an ISO 3166-1 country.</summary>
internal sealed class CountryRow
{
    public string CountryCode { get; init; } = null!;

    public string Alpha3 { get; init; } = null!;

    public string NumericCode { get; init; } = null!;

    public string? DefaultCurrencyCode { get; init; }

    public string? CallingCode { get; init; }

    public ReferenceStatus Status { get; init; }
}
