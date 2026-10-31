using Frappe.Geo.Contracts;

namespace Frappe.Geo.Infrastructure.Currencies;

/// <summary>A row of <c>geo.currencies</c>: an ISO 4217 currency, active (List One) or retired.</summary>
internal sealed class CurrencyRow
{
    public string CurrencyCode { get; init; } = null!;

    public string? NumericCode { get; init; }

    public short? MinorUnit { get; init; }

    public ReferenceStatus Status { get; init; }
}
