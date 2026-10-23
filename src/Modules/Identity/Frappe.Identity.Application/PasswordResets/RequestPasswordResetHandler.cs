using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Identity.Domain.Principals;
using Wolverine;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// Issues a reset code to the owner of <see cref="RequestPasswordReset.Email"/>. Callers cannot tell whether the
/// account exists: an unknown or suspended address gets the same answer with a fresh id, and the limits count every
/// request, known or not.
/// </summary>
public static class RequestPasswordResetHandler
{
    public static async Task<(PasswordResetRequested, OutgoingMessages)> Handle(
        RequestPasswordReset command,
        IPrincipalRepository principals,
        IAttemptLimiter limiter,
        ISecretGenerator secrets,
        ISecretHasher hasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var messages = new OutgoingMessages();
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return (PasswordResetRequested.Invalid(email), messages);

        var perEmail = await limiter.RecordAsync(AttemptPolicies.PasswordResetPerEmail, email.Value.Value, cancellationToken);
        var perIp = await limiter.RecordAsync(AttemptPolicies.PasswordResetPerIp, command.ClientIp, cancellationToken);
        if (!perEmail.IsAllowed || !perIp.IsAllowed)
            return (PasswordResetRequested.Limited(perEmail.RetryAfter > perIp.RetryAfter ? perEmail.RetryAfter : perIp.RetryAfter), messages);

        var anonymous = PasswordResetRequested.Accepted(Guid.CreateVersion7(clock.GetUtcNow()));
        var principal = await principals.GetByEmailAsync(email.Value, cancellationToken);
        if (principal is null || principal.Owner.Status == OwnerStatus.Suspended)
            return (anonymous, messages);

        var code = secrets.NewVerificationCode();
        var issued = principal.IssueCode(CodePurpose.PasswordReset, hasher.Hash(code), clock);
        // Hitting the domain's resend cap must look like any other accepted request.
        if (issued.IsFailure)
            return (anonymous, messages);

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Saving a reset code failed: {saved.Errors[0].Code}.");

        messages.Add(new PasswordResetCodeIssued(
            issued.Value.Id, principal.Id, principal.Owner.Email.Value, principal.Owner.Name.Value, code, issued.Value.ExpiresAt,
            principal.Owner.Locale.Value));
        return (PasswordResetRequested.Accepted(issued.Value.Id), messages);
    }
}
