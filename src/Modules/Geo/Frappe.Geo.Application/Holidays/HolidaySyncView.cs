using NodaTime;

namespace Frappe.Geo.Application.Holidays;

/// <summary>A holiday source: when it last ran, how that ended, and when it last succeeded.</summary>
public sealed record HolidaySyncView(string SourceId, Instant? LastAttemptAt, string? LastOutcome, Instant? LastSuccessAt);
