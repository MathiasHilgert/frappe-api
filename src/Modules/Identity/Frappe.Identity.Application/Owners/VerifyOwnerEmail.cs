using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Activates a pending owner with the code emailed at registration and signs them in at once (S7): the session is
/// returned for Bearer clients and handed to the web console as a cookie. Wrong codes are metered per owner.
/// </summary>
public sealed class VerifyOwnerEmail(
    IPrincipalRepository principals,
    ISecretHasher hasher,
    ISecretGenerator secrets,
    IAttemptLimiter limiter,
    ISessionCookie cookie,
    TimeProvider clock)
{
    public async Task<VerifyOwnerEmailOutcome> HandleAsync(Guid ownerId, string code, CancellationToken cancellationToken)
    {
        var policy = AttemptPolicies.VerifyEmailPerOwner;
        var key = ownerId.ToString();
        var decision = await limiter.CheckAsync(policy, key, cancellationToken);
        if (!decision.IsAllowed)
            return Failed(IdentityErrors.TooManyAttempts, decision.RetryAfter);

        var principal = await principals.GetByIdAsync(ownerId, cancellationToken);
        if (principal is null)
            return Failed(IdentityErrors.OwnerNotFound);

        var verified = principal.VerifyEmail(hasher.Hash(code), clock);
        if (verified.IsFailure)
        {
            if (verified.Errors.All(e => e != IdentityErrors.AccountNotPending))
                await limiter.RecordAsync(policy, key, cancellationToken);
            return new VerifyOwnerEmailOutcome(Result.Failure<OpenedSession>(verified.Errors[0]), TimeSpan.Zero);
        }

        var token = secrets.NewSessionToken();
        var session = principal.OpenSession(hasher.Hash(token), SessionSurface.Console, clock).Value;
        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            return new VerifyOwnerEmailOutcome(Result.Failure<OpenedSession>(saved.Errors[0]), TimeSpan.Zero);

        await limiter.ResetAsync(policy, key, cancellationToken);
        cookie.Issue(token, session.AbsoluteExpiresAt);
        return new VerifyOwnerEmailOutcome(Result.Success(new OpenedSession(session.Id, token, session.AbsoluteExpiresAt)), TimeSpan.Zero);
    }

    private static VerifyOwnerEmailOutcome Failed(Error error, TimeSpan retryAfter = default) =>
        new(Result.Failure<OpenedSession>(error), retryAfter);
}
