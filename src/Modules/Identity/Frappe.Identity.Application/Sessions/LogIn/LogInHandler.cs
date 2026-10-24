using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Failures are counted per email and IP together (the tight budget), per email alone (a much higher ceiling only a
/// distributed attack reaches) and per IP; each slows the caller down with a retry-after and frees itself when its
/// window ends. Keying the tight budget on the IP too means an attacker cannot lock the owner out from elsewhere. A
/// success clears that email and IP's count. Each attempt reserves its slot before the password is checked, so a parallel
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
        IIdentityTelemetry telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // The same normalized address the account is stored under, so case or spacing never buys another budget.
        var parsed = Email.Create(command.Email);
        var emailKey = parsed.TryGetValue(out var normalized) ? normalized.Value : command.Email.Trim().ToLowerInvariant();
        var reservation = await AttemptReservation.TakeAsync(limiter,
            [
                (AttemptPolicies.LoginFailuresPerEmailAndIp, $"{emailKey}|{command.ClientIp}"),
                (AttemptPolicies.LoginFailuresPerEmail, emailKey),
                (AttemptPolicies.LoginFailuresPerIp, command.ClientIp),
            ],
            cancellationToken);
        if (!reservation.IsAllowed)
        {
            telemetry.LoginThrottled();
            return Result.Failure<SignedIn>(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));
        }

        // A wrong password keeps its reserved slots: that is the failure being counted.
        var principal = await ProveAsync(normalized, command.Password, principals, passwordHasher, decoy, cancellationToken);
        if (principal is null)
            return Result.Failure<SignedIn>(IdentityErrors.InvalidCredentials);

        await reservation.SucceededAsync(AttemptPolicies.LoginFailuresPerEmailAndIp);

        var token = secrets.NewSessionToken();
        var opened = principal.OpenSession(secretHasher.Hash(token), command.Surface, clock.GetUtcNow());
        if (opened.IsFailure)
            return Result.Failure<SignedIn>(opened.Errors.Select(e => e.WithMetadata(IdentityErrorMetadata.OwnerId, principal.Id)));

        // The password is in hand, so this is the one moment an outdated hash can be upgraded to the current cost.
        if (passwordHasher.NeedsRehash(principal.Owner.PasswordHash))
            principal.RehashPassword(await passwordHasher.HashAsync(command.Password, cancellationToken));

        var saved = await principals.SaveAsync(cancellationToken);
        if (saved.IsFailure)
            return Result.Failure<SignedIn>(saved.Errors);
        var session = opened.Value;
        return Result.Success(new SignedIn(principal.Id, session.Id, token, session.AbsoluteExpiresAt));
    }

    /// <summary>The principal whose password this is, or null; an unknown email still pays for one hash check.</summary>
    private static async Task<Principal?> ProveAsync(
        Email? email, string password, IPrincipalRepository principals, IPasswordHasher hasher, DecoyPassword decoy,
        CancellationToken cancellationToken)
    {
        var principal = email is null ? null : await principals.GetByEmailAsync(email, cancellationToken);
        // Argon2 cannot derive from an empty secret; an empty password is simply wrong.
        if (password.Length == 0)
            return null;
        if (principal is null)
        {
            await decoy.VerifyAsync(password, cancellationToken);
            return null;
        }

        return await hasher.VerifyAsync(password, principal.Owner.PasswordHash, cancellationToken) ? principal : null;
    }
}
