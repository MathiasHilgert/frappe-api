using NodaTime;

namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>A row of <c>geo.sync_requests</c>: a run of a source asked for by hand, until the Worker starts it.</summary>
internal sealed class SyncRequestRow
{
    public required Guid Id { get; init; }

    public required string SourceId { get; init; }

    public required Instant RequestedAt { get; init; }
}
