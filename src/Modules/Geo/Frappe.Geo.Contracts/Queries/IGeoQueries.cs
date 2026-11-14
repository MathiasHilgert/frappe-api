namespace Frappe.Geo.Contracts.Queries;

/// <summary>
/// Geo's synchronous public queries for other modules, in process (ADR 0002): a country, a subdivision, whether a date
/// is a business day, the next business days, and, for payroll, what kind of day a date is. A jurisdiction is a country
/// (<c>AR</c>) or a first-level subdivision (<c>AR-X</c>). An answer never turns a gap in Geo's data into "no holidays":
/// a calendar Geo does not hold answers <see cref="CalendarStatus.NotCovered"/>.
/// </summary>
public interface IGeoQueries
{
    /// <summary>The most business days <see cref="NextBusinessDaysAsync"/> answers at once.</summary>
    public const int MaxBusinessDays = 60;

    /// <summary>The country of an ISO 3166-1 alpha-2 code, with what Geo covers for it; <see langword="null"/> when unknown.</summary>
    Task<GeoCountry?> FindCountryAsync(string countryCode, CancellationToken cancellationToken);

    /// <summary>The subdivision of an ISO 3166-2 code; <see langword="null"/> when unknown or malformed.</summary>
    Task<GeoSubdivision?> FindSubdivisionAsync(string subdivisionCode, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="date"/> is a business day in <paramref name="jurisdiction"/> under <paramref name="policy"/>.</summary>
    Task<BusinessDayAnswer> IsBusinessDayAsync(string jurisdiction, DateOnly date, BusinessDayPolicy policy, CancellationToken cancellationToken);

    /// <summary>
    /// The next <paramref name="count"/> business days from <paramref name="from"/> (included), 1 to
    /// <see cref="MaxBusinessDays"/>; when a year on the way is not covered, the answer is not covered and holds no dates.
    /// </summary>
    Task<BusinessDaysAnswer> NextBusinessDaysAsync(string jurisdiction, DateOnly from, int count, BusinessDayPolicy policy, CancellationToken cancellationToken);

    /// <summary>What kind of day <paramref name="date"/> is for payroll (the labor policy): its strongest observance's kind.</summary>
    Task<DayKindAnswer> GetDayKindAsync(string jurisdiction, DateOnly date, CancellationToken cancellationToken);
}
