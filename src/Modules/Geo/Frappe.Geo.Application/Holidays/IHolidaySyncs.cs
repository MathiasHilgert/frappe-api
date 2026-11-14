namespace Frappe.Geo.Application.Holidays;

/// <summary>The holiday sources the Worker syncs, their run history, and the runs requested by hand.</summary>
public interface IHolidaySyncs
{
    /// <summary>Every holiday source that ran at least once, by id.</summary>
    Task<IReadOnlyList<HolidaySyncView>> ListAsync(CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>The source's latest runs, newest first.</summary>
    Task<IReadOnlyList<SyncRunView>> RunsAsync(string sourceId, int limit, CancellationToken cancellationToken);

    /// <summary>Stages a request for a run, which the Worker picks up and starts; the unit of work saves it.</summary>
    SyncRequestView RequestRun(string sourceId);
}
