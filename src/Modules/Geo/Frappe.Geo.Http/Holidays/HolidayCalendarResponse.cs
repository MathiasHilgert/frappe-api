namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// The observances that apply in a jurisdiction in a year, by date. <c>covers</c> names the calendars they come from:
/// the country's, then the subdivision's own when Geo has it. <c>attributions</c> credits the sources of those
/// calendars, as their terms ask: show it wherever the observances are shown. <c>scope</c> is the calendars' scope
/// (<c>general</c>, or a sector's such as <c>banking</c>, which lists its source's facts as published).
/// </summary>
public sealed record HolidayCalendarResponse(
    string Jurisdiction,
    int Year,
    string Scope,
    IReadOnlyList<string> Covers,
    IReadOnlyList<AttributionResponse> Attributions,
    IReadOnlyList<ObservanceResponse> Observances);
