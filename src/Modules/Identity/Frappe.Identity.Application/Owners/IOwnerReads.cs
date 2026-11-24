namespace Frappe.Identity.Application.Owners;

/// <summary>What queries read about owners, projected without loading an aggregate (CQRS-08). It never changes anything (CQRS-07).</summary>
public interface IOwnerReads
{
    /// <summary>
    /// The owner whose principal is <paramref name="principalId"/> with their address, or <see langword="null"/> when there is none.
    /// </summary>
    Task<OwnerProfile?> FindProfileAsync(Guid principalId, CancellationToken cancellationToken);
}
