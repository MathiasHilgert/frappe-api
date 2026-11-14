namespace Frappe.Geo.Http.Holidays;

/// <summary>The holiday sources, by id.</summary>
public sealed record HolidaySyncListResponse(IReadOnlyList<HolidaySyncResponse> Sources);
