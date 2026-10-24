using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Owners;

/// <summary>Wrong codes are metered per owner; a right one clears the count.</summary>
public static class VerifyOwnerEmailHandler
{
    public static async Task<Result<OpenedSession>> Handle(
        VerifyOwnerEmail command,
        IPrincipalRepository principals,
        ISecretHasher hasher,
        ISecretGenerator secrets,
        IAttemptLimiter limiter,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var policy = AttemptPolicies.VerifyEmailPerOwner;
        var key = command.OwnerId.ToString();
        var decision = await limiter.CheckAsync(policy, key, cancellationToken);
        if (!decision.IsAllowed)
            return Failed(IdentityErrors.TooManyAttempts.WithRetryAfter(decision.RetryAfter));

        var principal = await principals.GetByIdAsync(command.OwnerId, cancellationToken);
        if (principal is null)
            return Failed(IdentityErrors.OwnerNotFound);

        var verified = principal.VerifyEmail(hasher.Hash(command.Code), clock);
        if (verified.IsFailure)
        {
            if (verified.Errors.All(e => e != IdentityErrors.AccountNotPending))
                await limiter.RecordAsync(policy, key, cancellationToken);
            return Failed(verified.Errors[0]);
        }

        var token = secrets.NewSessionToken();
        var session = principal.OpenSession(hasher.Hash(token), SessionSurface.Console, clock).Value;
        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            return Failed(saved.Errors[0]);

        await limiter.ResetAsync(policy, key, cancellationToken);
        return Result.Success(new OpenedSession(session.Id, token, session.AbsoluteExpiresAt));
    }

    private static Result<OpenedSession> Failed(Error error) => Result.Failure<OpenedSession>(error);
}
