namespace Frappe.Identity.Contracts.Queries;

/// <summary>
/// Synchronous, in-process reads another module may make of identity: the pattern for cross-module queries. The
/// interface lives in Contracts; identity implements it in Infrastructure against its own schema, so the caller never
/// sees identity's tables. Use it for what an event must not carry (personal data, see docs/conventions.md).
/// </summary>
public interface IOwnerDirectory
{
    /// <summary>How to reach an owner right now, or null when the owner no longer exists (a purged registration).</summary>
    /// <param name="principalId">The owner's principal id, as events carry it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<OwnerContact?> FindContactAsync(Guid principalId, CancellationToken cancellationToken);
}
