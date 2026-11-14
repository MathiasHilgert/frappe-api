namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// An observance set by hand: <c>date</c> (<c>yyyy-MM-dd</c>) and <c>name</c> identify it; <c>kind</c> is
/// <c>holiday</c>, <c>tourist_non_working_day</c>, <c>non_working_day</c> or <c>community_non_working_day</c> (which
/// needs <c>community</c>, one the country declares); <c>movedFrom</c> is the original date of a moved holiday. Date, name
/// and kind are required; the rest are optional.
/// </summary>
public sealed record ObservanceOverrideRequest(
    string? Date, string? Name, string? Kind, string? MovedFrom = null, string? Community = null, LegalBasisRequest? LegalBasis = null);
