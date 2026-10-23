using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Principals;

/// <summary>Loads and stores whole <see cref="Principal"/> aggregates, sessions and codes included.</summary>
public interface IPrincipalRepository
{
    Task<Principal?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Matches the current address ignoring case, the same rule the unique index enforces.</summary>
    Task<Principal?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<Principal?> GetBySessionTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>The owner of the live code <paramref name="codeId"/> issued for <paramref name="purpose"/>.</summary>
    Task<Principal?> GetByCodeIdAsync(Guid codeId, CodePurpose purpose, CancellationToken cancellationToken);

    void Add(Principal principal);

    /// <summary>Commits tracked changes. An address already taken fails with <c>IdentityErrors.EmailTaken</c>.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
