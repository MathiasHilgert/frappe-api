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

    /// <summary>Matches the current address; <see cref="Email"/> is already normalized, as the unique index sees it.</summary>
    Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// The lean, read-only view of the session holding <paramref name="tokenHash"/>, for authenticating a request
    /// without loading (or tracking) its whole aggregate.
    /// </summary>
    Task<SessionCredential?> FindSessionCredentialAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The owner of the live code <paramref name="codeId"/> issued for <paramref name="purpose"/>.</summary>
    Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken);

    /// <summary>
    /// Pending owners registered before <paramref name="cutoff"/>, ordered by (registered at, id) and starting after
    /// <paramref name="after"/> (keyset paging), at most <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<PurgeCandidate>> ListPurgeCandidatesAsync(
        DateTimeOffset cutoff, PurgeCandidate? after, int limit, CancellationToken cancellationToken);

    void Add(Principal principal);

    void Remove(Principal principal);

    /// <summary>
    /// Commits tracked changes, the one save every caller uses. It fails, with nothing saved and the rejected state no
    /// longer tracked, with <c>IdentityErrors.ConcurrentChange</c> when someone else changed the principal since it was
    /// loaded, or <c>IdentityErrors.EmailTaken</c> when another owner holds an address this save claims (a
    /// registration, a confirmed email change). Best-effort writers (a session touch) just ignore the failure. Any other
    /// error throws, with the failed state no longer tracked and, inside a transaction, the transaction still usable.
    /// </summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken);
}
