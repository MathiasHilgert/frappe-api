using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Principals;

/// <summary>
/// Loads and stores whole <see cref="Principal"/> aggregates, sessions and codes included. Every save is guarded by
/// optimistic concurrency: a principal changed by someone else since it was loaded is never silently overwritten. The
/// save methods differ only in which conflicts they expect, so the signature says what a caller must handle.
/// </summary>
public interface IPrincipalRepository
{
    Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Matches the current address ignoring case, the same rule the unique index enforces.</summary>
    Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// The lean, read-only view of the session holding <paramref name="tokenHash"/>, for authenticating a request
    /// without loading (or tracking) its whole aggregate.
    /// </summary>
    Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The owner of the live code <paramref name="codeId"/> issued for <paramref name="purpose"/>.</summary>
    Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken);

    /// <summary>Pending owners registered before <paramref name="cutoff"/>, oldest first, at most <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Principal>> ListPendingRegisteredBeforeAsync(
        DateTimeOffset cutoff, int limit, CancellationToken cancellationToken);

    void Add(Principal principal);

    void Remove(Principal principal);

    /// <summary>Commits tracked changes that cannot conflict with another owner; any failure is exceptional.</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits tracked changes that claim an email address (a registration, a confirmed email change). Another owner
    /// holding the address fails with <c>IdentityErrors.EmailTaken</c>; the rejected rows are no longer tracked.
    /// </summary>
    Task<Result> SaveClaimingEmailAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits tracked changes unless the principal was changed by someone else since it was loaded (a session
    /// revoked, a password reset). False then, and nothing was saved: for best-effort writes such as a session touch.
    /// </summary>
    Task<bool> SaveUnlessChangedAsync(CancellationToken cancellationToken);

    /// <summary>Same as <see cref="SaveClaimingEmailAsync"/>; kept for callers not yet moved to the explicit methods.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
