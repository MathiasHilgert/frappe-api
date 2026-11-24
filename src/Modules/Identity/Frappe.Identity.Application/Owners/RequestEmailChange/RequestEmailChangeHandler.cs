using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.RequestEmailChange;

/// <summary>
/// Handles <see cref="RequestEmailChange"/>. A new address that is free (or held by a pending owner, whom the confirmation replaces) gets a code;
/// one that a verified owner (or the caller) holds gets none, and every other effect is the same (SEC-06): the send is counted, the live code is
/// invalidated, the old address is told and the answer is a fresh UUIDv7. The time of both is equalized around the whole command
/// (<see cref="EmailChangeLimits.Floor"/>).
/// </summary>
internal sealed class RequestEmailChangeHandler(
    IPrincipals principals, IOwners owners, IEmailIdentifiers identifiers, CodeIssuer codes, TimeProvider time)
    : ICommandHandler<RequestEmailChange, EmailChangeRequested>
{
    public async Task<Result<EmailChangeRequested>> HandleAsync(RequestEmailChange command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = Email.Create(command.NewEmail);
        if (!email.IsSuccess)
        {
            return Result.Failure<EmailChangeRequested>(email.Errors);
        }

        if (await LoadAsync(command.PrincipalId, cancellationToken) is not var (principal, owner, current))
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        var holder = await identifiers.FindByAddressAsync(email.Value.NormalizedAddress, cancellationToken);
        var taken = holder is not null && (holder.PrincipalId == principal.Id || holder.VerifiedAt is not null);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var sent = taken
            ? await SpendAsync(principal.Id, cancellationToken)
            : await SendAsync(owner, email.Value, now, cancellationToken);
        if (!sent.IsSuccess)
        {
            return Result.Failure<EmailChangeRequested>(sent.Errors);
        }

        owner.RecordEmailChangeRequested(current.Address, email.Value, now);
        return new EmailChangeRequested(sent.Value);
    }

    /// <summary>The taken branch: the send is counted and the live code invalidated, no code is made, and the id is made up.</summary>
    private async Task<Result<Guid>> SpendAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var spent = await codes.SpendWithoutIssuingAsync(principalId, CodePurpose.EmailChange, cancellationToken);
        return spent.IsSuccess ? Guid.CreateVersion7() : Result.Failure<Guid>(spent.Errors);
    }

    /// <summary>The free branch: a code for the new address, mailed through the owner's event; its id is the answer.</summary>
    private async Task<Result<Guid>> SendAsync(Owner owner, Email email, Instant now, CancellationToken cancellationToken)
    {
        var issued = await codes.IssueAsync(owner.PrincipalId, CodePurpose.EmailChange, email.NormalizedAddress, cancellationToken);
        if (!issued.IsSuccess)
        {
            return Result.Failure<Guid>(issued.Errors);
        }

        owner.RecordCodeIssued(CodePurpose.EmailChange, email.Address, issued.Value.Plaintext, now);
        return issued.Value.Id;
    }

    /// <summary>The active owner of <paramref name="principalId"/> with the address in force, or <see langword="null"/>.</summary>
    private async Task<(Principal Principal, Owner Owner, EmailIdentifier Current)?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var principal = await principals.FindAsync(principalId, cancellationToken);
        var owner = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Active }
            ? await owners.FindAsync(principal.Id, cancellationToken)
            : null;
        var current = owner is null ? null : await identifiers.FindOfAsync(principalId, cancellationToken);
        return principal is null || owner is null || current is null ? null : (principal, owner, current);
    }
}
