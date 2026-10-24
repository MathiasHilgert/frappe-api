using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.PasswordResets;

/// <summary>
/// Accepts a reset request for <see cref="RequestPasswordReset.Email"/>. Callers cannot tell whether the account
/// exists: every well-formed address does exactly the same work here (count the limits, queue
/// <see cref="IssuePasswordResetCode"/>) and gets a fresh id; the lookup and the code happen in the background. The
/// limits count every request, known or not.
/// </summary>
public static class RequestPasswordResetHandler
{
    public static async Task<(Result<Guid>, OutgoingMessages)> Handle(
        RequestPasswordReset command,
        IAttemptLimiter limiter,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var messages = new OutgoingMessages();
        var email = Email.Create(command.Email);
        if (email.IsFailure)
            return (Result.Failure<Guid>(email.Errors), messages);

        var perEmail = await limiter.RecordAsync(AttemptPolicies.PasswordResetPerEmail, email.Value.Value, cancellationToken);
        var perIp = await limiter.RecordAsync(AttemptPolicies.PasswordResetPerIp, command.ClientIp, cancellationToken);
        if (!perEmail.IsAllowed || !perIp.IsAllowed)
        {
            return (Result.Failure<Guid>(IdentityErrors.TooManyAttempts.WithRetryAfter(
                perEmail.RetryAfter > perIp.RetryAfter ? perEmail.RetryAfter : perIp.RetryAfter)), messages);
        }

        var resetId = Guid.CreateVersion7(clock.GetUtcNow());
        messages.Add(new IssuePasswordResetCode(resetId, email.Value.Value));
        return (Result.Success(resetId), messages);
    }
}
