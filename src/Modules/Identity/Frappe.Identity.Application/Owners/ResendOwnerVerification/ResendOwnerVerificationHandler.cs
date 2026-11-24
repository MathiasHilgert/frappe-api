using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.ResendOwnerVerification;

/// <summary>
/// Handles <see cref="ResendOwnerVerification"/>: a new code for a pending owner replaces the live one. Everything that stops it (no such owner,
/// already verified, the budget of 5 sends an hour) is a silent success.
/// </summary>
internal sealed class ResendOwnerVerificationHandler(
    IPrincipals principals, IOwners owners, IEmailIdentifiers identifiers, CodeIssuer codes, TimeProvider time)
    : ICommandHandler<ResendOwnerVerification, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResendOwnerVerification command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Guid.TryParse(command.OwnerId, out var id))
        {
            return Unit.Value;
        }

        var principal = await principals.FindAsync(id, cancellationToken);
        var pending = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Pending };
        var owner = pending ? await owners.FindAsync(id, cancellationToken) : null;
        var identifier = owner is null ? null : await identifiers.FindOfAsync(id, cancellationToken);
        if (owner is null || identifier is null)
        {
            return Unit.Value;
        }

        var issued = await codes.IssueAsync(id, CodePurpose.EmailVerification, identifier.NormalizedAddress, cancellationToken);
        if (issued.IsSuccess)
        {
            owner.RecordCodeIssued(
                CodePurpose.EmailVerification, identifier.Address, issued.Value.Plaintext, Instant.FromDateTimeOffset(time.GetUtcNow()));
        }

        return Unit.Value;
    }
}
