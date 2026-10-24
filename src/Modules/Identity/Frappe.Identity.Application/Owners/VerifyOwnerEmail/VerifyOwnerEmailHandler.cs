using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Wrong codes are metered per owner; a right one clears the count. Each attempt reserves its slot before the code is
/// checked, so a parallel burst cannot get more guesses than the limit.
/// </summary>
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
        var reservation = await AttemptReservation.TakeAsync(limiter, cancellationToken, (policy, command.OwnerId.ToString()));
        if (!reservation.IsAllowed)
            return Failed(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));

        var principal = await principals.GetByIdAsync(command.OwnerId, cancellationToken);
        if (principal is null)
        {
            await reservation.ReleaseAsync(cancellationToken);
            return Failed(IdentityErrors.OwnerNotFound);
        }

        var verified = principal.VerifyEmail(hasher.Hash(command.Code), clock.GetUtcNow());
        if (verified.IsFailure)
        {
            // Only a wrong code is a guess; an owner who is no longer pending gives the slot back.
            if (verified.Has(IdentityErrors.AccountNotPending))
                await reservation.ReleaseAsync(cancellationToken);
            return Result.Failure<OpenedSession>(verified.Errors);
        }

        var token = secrets.NewSessionToken();
        var session = principal.OpenSession(hasher.Hash(token), SessionSurface.Console, clock.GetUtcNow()).Value;
        var saved = await principals.SaveAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // The code was right but nothing was saved: not a guess, and the owner can simply retry.
            await reservation.ReleaseAsync(cancellationToken);
            return Result.Failure<OpenedSession>(saved.Errors);
        }

        await reservation.SucceededAsync(policy, cancellationToken);
        return Result.Success(new OpenedSession(session.Id, token, session.AbsoluteExpiresAt));
    }

    private static Result<OpenedSession> Failed(Error error) => Result.Failure<OpenedSession>(error);
}
