using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Failures are counted per email and per IP and slow the caller down with a retry-after; nothing is ever locked, and
/// a success clears the email's count. Each attempt reserves its slot before the password is checked, so a parallel
/// burst cannot get more tries than the limit. Unknown email, malformed email and wrong password are deliberately the
/// same <see cref="IdentityErrors.InvalidCredentials"/>; only an attempt that proved the password learns the account's
/// state, with the owner id in the error's metadata under <see cref="IdentityErrorMetadata.OwnerId"/>.
/// </summary>
public static class LogInHandler
{

    public static async Task<Result<SignedIn>> Handle(
        LogIn command,
        IPrincipalRepository principals,
        IPasswordHasher passwordHasher,
        DecoyPassword decoy,
        ISecretHasher secretHasher,
        ISecretGenerator secrets,
        IAttemptLimiter limiter,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var reservation = await AttemptReservation.TakeAsync(limiter, cancellationToken,
            (AttemptPolicies.LoginFailuresPerEmail, command.Email.Trim()),
            (AttemptPolicies.LoginFailuresPerIp, command.ClientIp));
        if (!reservation.IsAllowed)
            return Result.Failure<SignedIn>(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));

        // A wrong password keeps its reserved slots: that is the failure being counted.
        var principal = await ProveAsync(command, principals, passwordHasher, decoy, cancellationToken);
        if (principal is null)
            return Result.Failure<SignedIn>(IdentityErrors.InvalidCredentials);

        await reservation.SucceededAsync(AttemptPolicies.LoginFailuresPerEmail, cancellationToken);

        var token = secrets.NewSessionToken();
        var opened = principal.OpenSession(secretHasher.Hash(token), command.Surface, clock);
        if (opened.IsFailure)
            return Result.Failure<SignedIn>(opened.Errors[0].WithMetadata(IdentityErrorMetadata.OwnerId, principal.Id));

        // The password is in hand, so this is the one moment an outdated hash can be upgraded to the current cost.
        if (passwordHasher.NeedsRehash(principal.Owner.PasswordHash))
            principal.RehashPassword(await passwordHasher.HashAsync(command.Password, cancellationToken));

        var saved = await principals.SaveAsync(cancellationToken);
        if (saved.IsFailure)
            return Result.Failure<SignedIn>(saved.Errors[0]);
        var session = opened.Value;
        return Result.Success(new SignedIn(principal.Id, session.Id, token, session.AbsoluteExpiresAt));
    }

    /// <summary>The principal whose password this is, or null; an unknown email still pays for one hash check.</summary>
    private static async Task<Principal?> ProveAsync(
        LogIn command, IPrincipalRepository principals, IPasswordHasher hasher, DecoyPassword decoy,
        CancellationToken cancellationToken)
    {
        var parsed = Email.Create(command.Email);
        var principal = parsed.TryGetValue(out var email) ? await principals.GetByEmailAsync(email, cancellationToken) : null;
        // Argon2 cannot derive from an empty secret; an empty password is simply wrong.
        if (command.Password.Length == 0)
            return null;
        if (principal is null)
        {
            await decoy.VerifyAsync(command.Password, cancellationToken);
            return null;
        }

        return await hasher.VerifyAsync(command.Password, principal.Owner.PasswordHash, cancellationToken) ? principal : null;
    }
}
