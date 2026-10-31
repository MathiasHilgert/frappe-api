namespace Frappe.Geo.Domain.Holidays;

/// <summary>The errors of the holiday catalogue.</summary>
public static class HolidayErrors
{
    /// <summary>Gets the error for a year outside 1 to 9999.</summary>
    public static Error YearInvalid { get; } = Error.Validation("geo.holiday.year_invalid");

    /// <summary>Gets the error for a year whose calendar is not seeded, so it never reads as a year without holidays.</summary>
    public static Error YearUnavailable { get; } = Error.NotFound("geo.holiday.year_unavailable");
}
