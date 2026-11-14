namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// One audited run: its outcome, the calendars it opened (<c>inserted</c>) and changed (<c>updated</c>), why it failed
/// or was refused (<c>error</c>), and the manual overrides the source changed something under (<c>conflicts</c>).
/// </summary>
public sealed record SyncRunResponse(string Id, string StartedAt, string FinishedAt, string Outcome, int Inserted, int Updated, string? Error, IReadOnlyList<OverrideConflictResponse> Conflicts);
