using Frappe.Geo.Http.Holidays;

namespace Frappe.Geo.Http.BusinessDays;

/// <summary>
/// The next business days in a jurisdiction under a policy, from <c>from</c> (included), in order. <c>covers</c> names
/// the general calendars read; <c>attributions</c> credits the sources of every calendar read, as their terms ask: show
/// it wherever these dates are shown.
/// </summary>
public sealed record BusinessDaysResponse(
    string Jurisdiction,
    string Policy,
    string From,
    IReadOnlyList<string> Dates,
    IReadOnlyList<string> Covers,
    IReadOnlyList<AttributionResponse> Attributions);
