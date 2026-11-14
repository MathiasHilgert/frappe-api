using NodaTime;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>A row of <c>geo.sync_runs</c>: the audit of one run of one source, failed and refused runs included.</summary>
internal sealed class SyncRunRow
{
    public const int MaxErrorLength = 2000;

    public required Guid Id { get; init; }

    public required string SourceId { get; init; }

    public required Instant StartedAt { get; init; }

    public Instant FinishedAt { get; set; }

    public SyncOutcome Outcome { get; set; }

    public int Inserted { get; set; }

    public int Updated { get; set; }

    public int Retired { get; set; }

    public int Revived { get; set; }

    public string? Error { get; set; }

    /// <summary>The overrides the source changed something under (S9): they won, and someone should look at them.</summary>
    public List<SyncConflictRow> Conflicts { get; init; } = [];
}
