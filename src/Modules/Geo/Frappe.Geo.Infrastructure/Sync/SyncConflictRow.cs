namespace Frappe.Geo.Infrastructure.Sync;

/// <summary>One override conflict of a run, in <c>geo.sync_runs.conflicts</c> (JSON): the observance by jurisdiction, year, date and name.</summary>
internal sealed class SyncConflictRow
{
    public required string Jurisdiction { get; init; }

    public required int Year { get; init; }

    /// <summary>ISO date (<c>yyyy-MM-dd</c>).</summary>
    public required string Date { get; init; }

    public required string Name { get; init; }
}
