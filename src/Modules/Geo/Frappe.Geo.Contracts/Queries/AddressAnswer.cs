namespace Frappe.Geo.Contracts.Queries;

/// <summary>An address resolution; <see cref="Address"/> holds only when <see cref="Status"/> is resolved.</summary>
public sealed record AddressAnswer(AddressStatus Status, GeoAddress? Address);
