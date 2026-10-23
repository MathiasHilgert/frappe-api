using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Account;

/// <summary>
/// Parks a new address as pending and issues the <c>email_change</c> code for it; the current address keeps working
/// until the code comes back. Returns the change id (the live code's id) and publishes the code for the mailer through
/// the outbox, so it is sent only if the change was saved.
/// </summary>
public static class RequestEmailChangeHandler
{
    private static readonly AttemptPolicy Policy = AttemptPolicies.EmailChangeRequestsPerPrincipal;

    public static async Task<Throttled<Guid>> Handle(
        RequestEmailChange command,
        IPrincipalRepository principals,
        ISecretGenerator generator,
        ISecretHasher hasher,
        IAttemptLimiter limiter,
        TimeProvider clock,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        var gate = await limiter.RecordAsync(Policy, command.PrincipalId.ToString(), cancellationToken);
        if (!gate.IsAllowed)
            return new Throttled<Guid>(null, gate.RetryAfter);

        var email = Email.Create(command.NewEmail);
        if (email.IsFailure)
            return new Throttled<Guid>(Result.Failure<Guid>(email.Errors[0] with { Field = "newEmail" }));

        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return new Throttled<Guid>(Result.Failure<Guid>(IdentityErrors.SessionNotFound));
        // The owner's own current address counts as taken too: there is nothing to change.
        if (await principals.GetByEmailAsync(email.Value, cancellationToken) is not null)
            return new Throttled<Guid>(Result.Failure<Guid>(IdentityErrors.EmailTaken with { Field = "newEmail" }));

        var code = generator.NewVerificationCode();
        var issued = principal.RequestEmailChange(email.Value, hasher.Hash(code), clock);
        if (issued.IsFailure)
            return new Throttled<Guid>(Result.Failure<Guid>(issued.Errors[0]));

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            return new Throttled<Guid>(Result.Failure<Guid>(saved.Errors[0]));

        await messages.PublishAsync(new EmailChangeCodeIssued(
            issued.Value.Id, principal.Id, principal.Owner.Name.Value, email.Value.Value, code, issued.Value.ExpiresAt));
        return new Throttled<Guid>(Result.Success(issued.Value.Id));
    }
}
