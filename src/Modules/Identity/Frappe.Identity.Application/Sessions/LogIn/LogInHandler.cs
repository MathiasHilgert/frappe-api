using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Failures are counted per email and IP together (the tight budget), per email alone (a much higher ceiling only a
/// distributed attack reaches) and per IP; each slows the caller down with a retry-after and frees itself when its
/// window ends. Keying the tight budget on the IP too means an attacker cannot lock the owner out from elsewhere. The
/// email ceiling alone could be reached by an attacker on many addresses, so an origin this email already signed in
/// from (<see cref="IKnownSignInOrigins"/>) is exempt from it: the owner keeps signing in from the places they use,
/// while that origin still has its own tight budget. This is the server-side form of OWASP's "device cookie"
/// lockout defence; it needs no client support. A success clears that email and IP's count, and cascades
/// <see cref="RememberSignInOrigin"/>: the outbox sends it only after the session committed, so a rolled-back sign-in
/// never makes its origin known. Each attempt reserves its slot before the password is checked, so a parallel
/// burst cannot get more tries than the limit. Unknown email, malformed email and wrong password are deliberately the
/// same <see cref="IdentityErrors.InvalidCredentials"/>; only an attempt that proved the password learns the account's
/// state, with the owner id in the error's metadata under <see cref="IdentityErrorMetadata.OwnerId"/>.
/// </summary>
public static class LogInHandler
{
    public static async Task<(Result<SignedIn>, OutgoingMessages)> Handle(
        LogIn command,
        IPrincipalRepository principals,
        IPasswordHasher passwordHasher,
        DecoyPassword decoy,
        ISecretHasher secretHasher,
        ISecretGenerator secrets,
        IAttemptLimiter limiter,
        IKnownSignInOrigins knownOrigins,
        IIdentityTelemetry telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var signedIn = await SignInAsync(command, principals, passwordHasher, decoy, secretHasher, secrets, limiter,
            knownOrigins, telemetry, clock, cancellationToken);
        var messages = new OutgoingMessages();
        if (signedIn.IsSuccess)
            messages.Add(new RememberSignInOrigin(EmailKey(command.Email), command.ClientIp));
        return (signedIn, messages);
    }

    // The same normalized address the account is stored under, so case or spacing never buys another budget.
    private static string EmailKey(string? email) =>
        Email.Create(email).TryGetValue(out var normalized) ? normalized.Value : email?.Trim().ToLowerInvariant() ?? string.Empty;

    private static async Task<Result<SignedIn>> SignInAsync(
        LogIn command,
        IPrincipalRepository principals,
        IPasswordHasher passwordHasher,
        DecoyPassword decoy,
        ISecretHasher secretHasher,
        ISecretGenerator secrets,
        IAttemptLimiter limiter,
        IKnownSignInOrigins knownOrigins,
        IIdentityTelemetry telemetry,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        _ = Email.Create(command.Email).TryGetValue(out var normalized);
        var emailKey = EmailKey(command.Email);
        var limits = await LimitsAsync(emailKey, command.ClientIp, knownOrigins, cancellationToken);
        var reservation = await AttemptReservation.TakeAsync(limiter, limits, cancellationToken);
        if (!reservation.IsAllowed)
        {
            telemetry.LoginThrottled();
            return Result.Failure<SignedIn>(IdentityErrors.TooManyAttempts.WithRetryAfter(reservation.Decision.RetryAfter));
        }

        // A wrong password keeps its reserved slots: that is the failure being counted.
        var password = command.Password ?? string.Empty;
        var principal = await ProveAsync(normalized, password, principals, passwordHasher, decoy, cancellationToken);
        if (principal is null)
            return Result.Failure<SignedIn>(IdentityErrors.InvalidCredentials);

        await reservation.SucceededAsync(AttemptPolicies.LoginFailuresPerEmailAndIp);

        var token = secrets.NewSessionToken();
        var opened = principal.OpenSession(secretHasher.Hash(token), command.Surface, clock.GetUtcNow());
        if (opened.IsFailure)
            return Result.Failure<SignedIn>(opened.Errors.Select(e => e.WithMetadata(IdentityErrorMetadata.OwnerId, principal.Id)));

        // The password is in hand, so this is the one moment an outdated hash can be upgraded to the current cost.
        if (passwordHasher.NeedsRehash(principal.Owner.PasswordHash))
            principal.RehashPassword(await passwordHasher.HashAsync(password, cancellationToken));

        var saved = await principals.SaveAsync(cancellationToken);
        if (saved.IsFailure)
            return Result.Failure<SignedIn>(saved.Errors);
        var session = opened.Value;
        return Result.Success(new SignedIn(principal.Id, session.Id, token, session.AbsoluteExpiresAt));
    }

    /// <summary>The limits this attempt reserves; an origin the email already signed in from skips the email ceiling.</summary>
    private static async Task<List<(AttemptPolicy, string)>> LimitsAsync(
        string emailKey, string clientIp, IKnownSignInOrigins knownOrigins, CancellationToken cancellationToken)
    {
        var limits = new List<(AttemptPolicy, string)>
        {
            (AttemptPolicies.LoginFailuresPerEmailAndIp, $"{emailKey}|{clientIp}"),
            (AttemptPolicies.LoginFailuresPerIp, clientIp),
        };
        if (!await knownOrigins.IsKnownAsync(emailKey, clientIp, cancellationToken))
            limits.Add((AttemptPolicies.LoginFailuresPerEmail, emailKey));
        return limits;
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
