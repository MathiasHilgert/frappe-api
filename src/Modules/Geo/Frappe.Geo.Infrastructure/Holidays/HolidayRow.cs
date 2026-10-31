using Frappe.Geo.Contracts;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Holidays;

/// <summary>A row of <c>geo.holidays</c>: one public holiday on one day, never merged with another on that day.</summary>
internal sealed class HolidayRow
{
    public Guid HolidayId { get; init; }

    public string CountryCode { get; init; } = null!;

    // TODO: link holidays to subdivisions (FK + existence check) once T4 lands
    public string? SubdivisionCode { get; init; }

    public LocalDate Date { get; init; }

    /// <summary>Gets the slug of the English name, which tells apart two holidays on one day.</summary>
    public string HolidayKey { get; init; } = null!;

    public HolidayClass Class { get; init; }

    public HolidayOrigin Origin { get; init; }

    public ReferenceStatus Status { get; init; }
}
