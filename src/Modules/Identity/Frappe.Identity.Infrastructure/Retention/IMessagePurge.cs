namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// The purge of the module's outbox and inbox. The message purger lives in the broker side of messaging, which the Migrator may not load and
/// therefore this project may not reference; the Worker host implements this port over it and the retention job calls it.
/// </summary>
internal interface IMessagePurge
{
    /// <summary>Deletes the published outbox rows and inbox records past their retention.</summary>
    Task PurgeAsync(CancellationToken cancellationToken);
}
