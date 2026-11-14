namespace Frappe.Geo.Http.Holidays;

/// <summary>A source's latest runs, newest first.</summary>
public sealed record SyncRunListResponse(IReadOnlyList<SyncRunResponse> Runs);
