using NodaTime;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>
/// A row of <c>geo.sync_resources</c>: the state of one file of one source. The validators and the SHA-256 are those of
/// the last success, so a failed or refused attempt never makes the next run skip a file it did not apply.
/// </summary>
internal sealed class SyncResourceRow
{
    public required string SourceId { get; init; }

    public required string Resource { get; init; }

    /// <summary>What the source syncs (<c>SyncKinds</c>), so administration can tell holiday sources from the others.</summary>
    public string? Kind { get; set; }

    public string? ETag { get; set; }

    public DateTimeOffset? LastModified { get; set; }

    public string? Sha256 { get; set; }

    public Instant? LastAttemptAt { get; set; }

    public Instant? LastSuccessAt { get; set; }

    public string? LastOutcome { get; set; }
}
