namespace Frappe.Geo.Infrastructure.Holidays;

/// <summary>A row of <c>geo.holiday_names</c>: a holiday's name in one locale.</summary>
internal sealed class HolidayNameRow
{
    public Guid HolidayId { get; init; }

    public string Locale { get; init; } = null!;

    public string Name { get; init; } = null!;
}
