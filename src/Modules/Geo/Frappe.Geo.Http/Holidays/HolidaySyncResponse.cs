namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// A holiday source: when it last ran (<c>lastAttemptAt</c>), how that ended (<c>lastOutcome</c>: <c>applied</c>,
/// <c>unchanged</c>, <c>not_published</c>, <c>refused</c> or <c>failed</c>) and when it last succeeded.
/// </summary>
public sealed record HolidaySyncResponse(string SourceId, string? LastAttemptAt, string? LastOutcome, string? LastSuccessAt);
