using Frappe.Results;

namespace Frappe.Geo.Application.Sync;

/// <summary>
/// An external source of reference data, as an adapter declares it: where its files are and how to read them into a
/// canonical snapshot. It implements no process: downloading (conditionally), validating the whole, applying, keeping
/// state and auditing each run belong to the module's one sync pipeline.
/// </summary>
public interface ISnapshotSource<TSnapshot>
{
    /// <summary>Stable id of the source (<c>ar-georef-territory</c>), which keys its state and its runs.</summary>
    string Id { get; }

    IReadOnlyList<SnapshotResource> Resources { get; }

    /// <summary>
    /// Reads the downloaded files (by <see cref="SnapshotResource.Name"/>) into a snapshot, or the reasons the download is
    /// refused as a whole (<see cref="SnapshotRules"/>). A refused download changes nothing.
    /// </summary>
    Result<TSnapshot> Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files);
}
