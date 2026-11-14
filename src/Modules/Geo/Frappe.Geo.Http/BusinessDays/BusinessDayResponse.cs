using Frappe.Geo.Http.Holidays;

namespace Frappe.Geo.Http.BusinessDays;

/// <summary>
/// A date in a jurisdiction under a policy. <c>businessDay</c> answers the policy; <c>kind</c> is the day's strongest
/// observance (<c>holiday</c>, <c>tourist_non_working_day</c>, <c>non_working_day</c>, <c>community_non_working_day</c>;
/// null for an ordinary day), which is what payroll (<c>labor</c>) needs; <c>observances</c> are those the policy looked
/// at; <c>covers</c> names the general calendars read; <c>attributions</c> credits the sources of every calendar read, as
/// their terms ask: show it wherever the observances are shown.
/// </summary>
public sealed record BusinessDayResponse(
    string Jurisdiction,
    string Date,
    string Policy,
    bool BusinessDay,
    bool Weekend,
    string? Kind,
    IReadOnlyList<ObservanceResponse> Observances,
    IReadOnlyList<string> Covers,
    IReadOnlyList<AttributionResponse> Attributions);
