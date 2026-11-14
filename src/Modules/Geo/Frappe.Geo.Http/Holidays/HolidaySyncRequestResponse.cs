namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// A run asked for by hand: the request's id (<c>requestId</c>; requests made before the Worker starts are run once, so
/// it is not a run's id), the source, and when it was asked for. The run appears among the source's runs (Location)
/// once the Worker ran it.
/// </summary>
public sealed record HolidaySyncRequestResponse(string RequestId, string SourceId, string RequestedAt);
