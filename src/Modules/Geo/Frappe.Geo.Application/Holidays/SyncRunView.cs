using Frappe.Geo.Application.Sync;
using NodaTime;

namespace Frappe.Geo.Application.Holidays;

/// <summary>One audited run of a source, with the override conflicts it found.</summary>
public sealed record SyncRunView(
    Guid Id,
    Instant StartedAt,
    Instant FinishedAt,
    string Outcome,
    int Inserted,
    int Updated,
    string? Error,
    IReadOnlyList<OverrideConflict> Conflicts);
