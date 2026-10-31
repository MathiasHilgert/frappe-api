using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using NodaTime;

namespace Frappe.Geo.Application.Holidays;

/// <summary>A public holiday.</summary>
/// <param name="Date">The day, such as 2026-05-25.</param>
/// <param name="Class">holiday (feriado) or non_working_day (día no laborable).</param>
/// <param name="Origin">fixed (inamovible), movable (trasladable), bridge (feriado puente) or decreed (a one-off day by decree).</param>
/// <param name="SubdivisionCode">The ISO 3166-2 subdivision it applies to, or null for a national holiday.</param>
/// <param name="Name">The name in the requested locale, or the nearest one along its fallback chain.</param>
public sealed record HolidayDto(
    LocalDate Date,
    HolidayClass Class,
    HolidayOrigin Origin,
    SubdivisionCode? SubdivisionCode,
    string Name
);
