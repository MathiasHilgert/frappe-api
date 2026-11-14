namespace Frappe.Geo.Application.Sync;

/// <summary>
/// Where a kind of snapshot lands. It brings the stored data to the snapshot and says what changed; it never saves,
/// so the pipeline commits the snapshot and its sync state in one transaction.
/// </summary>
public interface ISnapshotTarget<in TSnapshot>
{
    /// <summary>What kind of data the target holds (<see cref="SyncKinds"/>): it groups the sources for administration.</summary>
    string Kind { get; }

    Task<SnapshotChanges> ApplyAsync(TSnapshot snapshot, CancellationToken cancellationToken);
}
