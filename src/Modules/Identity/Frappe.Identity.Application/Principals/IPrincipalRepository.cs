using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Principals;

/// <summary>
/// Loads and stores whole <see cref="Principal"/> aggregates, sessions and codes included. Every save is guarded by
/// optimistic concurrency: a principal changed by someone else since it was loaded is never silently overwritten; the
/// save answers <c>IdentityErrors.ConcurrentChange</c> (409) instead.
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

    /// <summary>
    /// Commits tracked changes. Fails with <c>IdentityErrors.ConcurrentChange</c> when someone else changed the
    /// principal since it was loaded; nothing is saved then and the stale state is no longer tracked.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="SaveAsync"/>, for changes that claim an email address (a registration, a confirmed email
    /// change): another owner holding the address also fails, with <c>IdentityErrors.EmailTaken</c>.
    /// </summary>
    Task<Result> SaveClaimingEmailAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="SaveAsync"/> as a yes/no for best-effort writes such as a session touch: false when the principal
    /// changed meanwhile (a session revoked, a password reset), and nothing was saved.
    /// </summary>
    Task<bool> SaveUnlessChangedAsync(CancellationToken cancellationToken);
}
