using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Application.Holidays;

/// <summary>One stored calendar as it counts: its jurisdiction (<c>AR</c>, <c>AR-X</c>), its source's credit and its effective observances.</summary>
public sealed record EffectiveCalendar(string Jurisdiction, Attribution? Attribution, IReadOnlyList<Observance> Observances)
{
    public static EffectiveCalendar Of(HolidayCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return new(calendar.Jurisdiction.Value, calendar.Attribution, calendar.Effective);
    }
}
