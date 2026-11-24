namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// One step of the daily retention job. The core's steps (the sweep, the purge of sessions and codes) and the Owners feature's (the purge of
/// pending owners) all implement it, so the job names none of them and the core never refers to Owners. They run in registration order.
/// </summary>
internal interface IRetentionStep
{
    /// <summary>Does the step's work once.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
