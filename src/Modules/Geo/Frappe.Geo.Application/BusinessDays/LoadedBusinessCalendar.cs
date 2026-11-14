using Frappe.Geo.Domain.BusinessDays;
using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>A business calendar with what it was read from: the general calendars it covers and the credit their sources ask for.</summary>
internal sealed record LoadedBusinessCalendar(BusinessCalendar Calendar, IReadOnlyList<string> Covers, IReadOnlyList<Attribution> Attributions);
