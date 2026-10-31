namespace Frappe.Geo.Application.Timezones.ListTimezones;

/// <summary>An IANA time zone.</summary>
/// <param name="Id">The IANA id, such as <c>America/Cordoba</c>.</param>
/// <param name="CanonicalId">The canonical zone an alias links to, or <see langword="null"/> for a canonical zone.</param>
public sealed record TimezoneDto(string Id, string? CanonicalId);
