using Frappe.Geo.Contracts.ValueObjects;
using NodaTime;

namespace Frappe.Geo.Contracts;

/// <summary>A public holiday of a country with its name in the asked locale.</summary>
/// <param name="Date">The day of the holiday.</param>
/// <param name="Class">Whether it is a paid holiday or a non-working day.</param>
/// <param name="Origin">How it got its date.</param>
/// <param name="Subdivision">The subdivision it applies to, or <see langword="null"/> for a national holiday.</param>
/// <param name="Name">The name in the asked locale, or the nearest one along its fallback chain.</param>
public sealed record GeoHoliday(
    LocalDate Date,
    HolidayClass Class,
    HolidayOrigin Origin,
    SubdivisionCode? Subdivision,
    string Name
);
