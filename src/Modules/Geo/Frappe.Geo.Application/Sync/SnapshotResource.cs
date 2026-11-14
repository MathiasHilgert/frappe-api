namespace Frappe.Geo.Application.Sync;

/// <summary>
/// One file a source publishes, by a stable <see cref="Name"/> (its sync state is kept under it) and its location. An
/// <see cref="Optional"/> file may not be published yet (next year's calendar): its absence leaves it out of the files
/// the source reads instead of stopping the run.
/// </summary>
public sealed record SnapshotResource(string Name, Uri Location, bool Optional = false);
