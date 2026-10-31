using Frappe.Geo.Application.Timezones.ListTimezones;

namespace Frappe.Geo.Infrastructure.Timezones.ListTimezones;

/// <summary>Every IANA time zone, in one page.</summary>
/// <param name="Items">The zones, ordered by id.</param>
/// <param name="NextCursor">Always <see langword="null"/>: the catalogue fits in one page.</param>
internal sealed record ListTimezonesResponse(IReadOnlyList<TimezoneDto> Items, string? NextCursor);
