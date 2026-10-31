namespace Frappe.Geo.Application.Timezones.ListTimezones;

/// <summary>Reads the timezone catalogue.</summary>
public interface IListTimezonesQueryService
{
    /// <summary>Lists every zone, ordered by id.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The zones.</returns>
    Task<IReadOnlyList<TimezoneDto>> ListAsync(CancellationToken cancellationToken);
}
