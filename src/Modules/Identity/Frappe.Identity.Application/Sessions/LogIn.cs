using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Signs an owner in with email and password (S5). Failures are counted per email and per IP and slow the caller
/// down with a retry-after; nothing is ever locked, and a success clears the email's count.
/// </summary>
public sealed class LogIn(
    IPrincipalRepository principals,
    IPasswordHasher passwordHasher,
    ISecretHasher secretHasher,
    ISecretGenerator secrets,
    IAttemptLimiter limiter,
    TimeProvider clock)
{
    // Hash of a random secret, verified against when the email is unknown so response time does not reveal it.
    private static string? s_decoyHash;

    public async Task<LogInOutcome> ExecuteAsync(
        string email, string password, string clientIp, SessionSurface surface, CancellationToken cancellationToken)
    {
        var emailKey = email.Trim();
        var perEmail = await limiter.CheckAsync(AttemptPolicies.LoginFailuresPerEmail, emailKey, cancellationToken);
        var perIp = await limiter.CheckAsync(AttemptPolicies.LoginFailuresPerIp, clientIp, cancellationToken);
        if (!perEmail.IsAllowed || !perIp.IsAllowed)
            return new LogInOutcome.Throttled(Max(perEmail.RetryAfter, perIp.RetryAfter));

        var principal = await ProveAsync(email, password, cancellationToken);
        if (principal is null)
        {
            await limiter.RecordAsync(AttemptPolicies.LoginFailuresPerEmail, emailKey, cancellationToken);
            await limiter.RecordAsync(AttemptPolicies.LoginFailuresPerIp, clientIp, cancellationToken);
            return new LogInOutcome.InvalidCredentials();
        }

        var token = secrets.NewSessionToken();
        var opened = principal.OpenSession(secretHasher.Hash(token), surface, clock);
        if (opened.IsFailure)
            return new LogInOutcome.Refused(principal.Id, opened.Errors[0]);

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            throw new InvalidOperationException($"Opening a session failed to save: {saved.Errors[0].Code}.");

        await limiter.ResetAsync(AttemptPolicies.LoginFailuresPerEmail, emailKey, cancellationToken);
        var session = opened.Value;
        return new LogInOutcome.Opened(principal.Id, session.Id, token, session.AbsoluteExpiresAt);
    }

    /// <summary>The principal whose password this is, or null; an unknown email still pays for one hash check.</summary>
    private async Task<Principal?> ProveAsync(string email, string password, CancellationToken cancellationToken)
    {
        var parsed = Email.Create(email);
        var principal = parsed.IsSuccess ? await principals.GetByEmailAsync(parsed.Value, cancellationToken) : null;
        var hash = principal?.Owner.PasswordHash ?? DecoyHash(passwordHasher, secrets);
        // Argon2 cannot derive from an empty secret; an empty password is simply wrong.
        return password.Length > 0 && passwordHasher.Verify(password, hash) ? principal : null;
    }

    private static string DecoyHash(IPasswordHasher hasher, ISecretGenerator generator) =>
        s_decoyHash ??= hasher.Hash(generator.NewSessionToken());

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
