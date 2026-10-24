using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Identity.Domain.Principals;
using Wolverine;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// The account-dependent half of a reset request. An unknown or suspended address, or an owner at the resend cap,
/// simply gets nothing: the requester was answered long ago and cannot tell.
/// </summary>
public static class IssuePasswordResetCodeHandler
{
    public static async Task<OutgoingMessages> Handle(
        IssuePasswordResetCode command,
        IPrincipalRepository principals,
        ISecretGenerator secrets,
        ISecretHasher hasher,
        IOwnerCodeSeal seal,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var messages = new OutgoingMessages();
        var email = Email.Create(command.Email);
        var principal = email.TryGetValue(out var address) ? await principals.GetByEmailAsync(address, cancellationToken) : null;
        if (principal is null || principal.Owner.Status == OwnerStatus.Suspended)
            return messages;

        var code = secrets.NewVerificationCode();
        var issued = principal.IssueCode(command.ResetId, CodePurpose.PasswordReset, hasher.Hash(code), clock);
        if (issued.IsFailure)
            return messages;

        // A concurrent change wins; the owner can ask again, and nothing half-issued is mailed.
        if ((await principals.SaveAsync(cancellationToken)).IsFailure)
            return messages;
        messages.Add(new PasswordResetCodeIssued(
            issued.Value.Id, principal.Id, principal.Owner.Email.Value, principal.Owner.Name.Value, seal.Seal(code),
            issued.Value.ExpiresAt, principal.Owner.Locale.Value));
        return messages;
    }
}
