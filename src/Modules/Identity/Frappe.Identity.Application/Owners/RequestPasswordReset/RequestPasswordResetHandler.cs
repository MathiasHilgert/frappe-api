using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.RequestPasswordReset;

/// <summary>
/// Handles <see cref="RequestPasswordReset"/>. The reset id is the id of the code, so the confirmation needs no principal; every branch that
/// sends no code answers a fresh UUIDv7 of the same shape (SEC-06). The time both branches take is equalized by the caller around the whole
/// command, commit included (<see cref="PasswordResetLimits.Floor"/>).
/// </summary>
internal sealed class RequestPasswordResetHandler(
    IEmailIdentifiers identifiers, IPrincipals principals, IOwners owners, CodeIssuer codes, TimeProvider time)
    : ICommandHandler<RequestPasswordReset, PasswordResetRequested>
{
    public async Task<Result<PasswordResetRequested>> HandleAsync(RequestPasswordReset command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = Email.Create(command.Email);
        var holder = email.IsSuccess ? await FindHolderAsync(email.Value.NormalizedAddress, cancellationToken) : null;
        if (holder is not var (principal, owner, identifier))
        {
            return Decoy();
        }

        var issued = await codes.IssueAsync(principal.Id, CodePurpose.PasswordReset, identifier.NormalizedAddress, cancellationToken);
        if (!issued.IsSuccess)
        {
            // The send budget is spent: no code goes out, and the answer must not say so.
            return Decoy();
        }

        owner.RecordCodeIssued(
            CodePurpose.PasswordReset, identifier.Address, issued.Value.Plaintext, Instant.FromDateTimeOffset(time.GetUtcNow()));
        return new PasswordResetRequested(issued.Value.Id);
    }

    private static PasswordResetRequested Decoy() => new(Guid.CreateVersion7());

    /// <summary>The active owner who has <paramref name="normalizedAddress"/>, or <see langword="null"/> when nobody may reset with it.</summary>
    private async Task<(Principal Principal, Domain.Owners.Owner Owner, EmailIdentifier Identifier)?> FindHolderAsync(
        string normalizedAddress, CancellationToken cancellationToken)
    {
        var identifier = await identifiers.FindByAddressAsync(normalizedAddress, cancellationToken);
        var principal = identifier is null ? null : await principals.FindAsync(identifier.PrincipalId, cancellationToken);
        var owner = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Active }
            ? await owners.FindAsync(principal.Id, cancellationToken)
            : null;
        return identifier is null || principal is null || owner is null ? null : (principal, owner, identifier);
    }
}
