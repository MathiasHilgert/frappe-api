using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Geo.Application.Timezones.ListTimezones;

/// <summary>Lists every IANA time zone, ordered by id.</summary>
// Reference data is the same for every business, so the query runs for no tenant and no principal.
[GlobalMessage]
public sealed record ListTimezonesQuery : IQuery<ListTimezonesQuery, IReadOnlyList<TimezoneDto>>
{
    /// <inheritdoc />
    public static Policy Policy => Policy.Anonymous;
}
