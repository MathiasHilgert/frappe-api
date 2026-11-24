using System.Net;
using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Limits;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.LogInOwner;

/// <summary>
/// Handles <see cref="LogInOwner"/>. The attempt is counted against the typed address first, before anything is looked up, so a throttled
/// address costs the same whether it exists or not. Then every failure (a malformed address, an unknown one, one that is not an owner's, a wrong
/// or missing password) goes through <see cref="PasswordCheck"/> with the same cost and the same floor and answers
/// <see cref="IdentityErrorCodes.CredentialsInvalid"/> (SEC-06). Refusals are counted here, since nothing commits to count them; a log-in that
/// succeeds is counted once its unit commits.
/// </summary>
internal sealed class LogInOwnerHandler(
    IEmailIdentifiers identifiers,
    IPrincipals principals,
    IOwners owners,
    IPasswordCredentials credentials,
    PasswordCheck passwordCheck,
    CodeIssuer codes,
    SessionOpener sessions,
    IAttemptLimiter limiter,
    IdentityMetrics metrics,
    TimeProvider time) : ICommandHandler<LogInOwner, LogInOutcome>
{
    public async Task<Result<LogInOutcome>> HandleAsync(LogInOwner command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = Email.Create(command.Email);
        var typed = email.IsSuccess ? email.Value.NormalizedAddress : command.Email;
        if (await CountAttemptAsync(typed, command.ClientAddress, cancellationToken) is { } refused)
        {
            return refused;
        }

        var holder = email.IsSuccess ? await FindHolderAsync(email.Value.NormalizedAddress, cancellationToken) : null;
        var stored = holder?.Hash is { } hash ? new PasswordHash(hash) : null;
        if (!await passwordCheck.MatchesAsync(stored, command.Password, cancellationToken) || holder is null)
        {
            metrics.LoggedIn(LoginOutcome.Invalid);
            return new UnauthenticatedError(IdentityErrorCodes.CredentialsInvalid);
        }

        var (principal, owner, identifier) = (holder.Principal, holder.Owner, holder.Identifier);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        if (principal.Status == PrincipalStatus.Pending)
        {
            return await RequestVerificationAsync(principal, owner, identifier, now, cancellationToken);
        }

        owner.RecordLogIn(verificationPending: false, now);
        return LogInOutcome.Opened(sessions.Open(principal.Id, principal.CredentialStamp, command.UserAgent, command.ClientAddress));
    }

    /// <summary>Counts the attempt; the error that refuses it, or <see langword="null"/> when it may go on.</summary>
    private async Task<Error?> CountAttemptAsync(string? typedAddress, IPAddress? client, CancellationToken cancellationToken)
    {
        // Flows are hit before any lookup, whether the address exists or not, so neither answer nor cost reveals it.
        // The per-email flow is charged only when the network flow allowed the attempt, so one source cannot spend the owner's cap.
        var verdict = await limiter.HitAsync(LogInLimits.Attempt, LogInLimits.Subject(typedAddress, client), cancellationToken);
        if (verdict.Outcome == AttemptOutcome.Allowed)
        {
            verdict = await limiter.HitAsync(LogInLimits.AttemptByEmail, LogInLimits.Subject(typedAddress), cancellationToken);
        }

        switch (verdict.Outcome)
        {
            case AttemptOutcome.Unavailable:
                return new UnavailableError(IdentityErrorCodes.AttemptsUnavailable);
            case AttemptOutcome.Refused:
                metrics.LoggedIn(LoginOutcome.Throttled);
                return new RateLimitedError(LogInLimits.ThrottledCode, verdict.RetryAfter);
            default:
                return null;
        }
    }

    /// <summary>
    /// The owner who has <paramref name="normalizedAddress"/> and their stored password, or <see langword="null"/> when nobody can log in with it.
    /// </summary>
    private async Task<Holder?> FindHolderAsync(string normalizedAddress, CancellationToken cancellationToken)
    {
        var identifier = await identifiers.FindByAddressAsync(normalizedAddress, cancellationToken);
        var principal = identifier is null ? null : await principals.FindAsync(identifier.PrincipalId, cancellationToken);
        var owner = principal is { Kind: PrincipalKind.Owner } ? await owners.FindAsync(principal.Id, cancellationToken) : null;
        var credential = owner is null ? null : await credentials.FindAsync(owner.PrincipalId, cancellationToken);
        return identifier is null || principal is null || owner is null || credential is null
            ? null
            : new Holder(principal, owner, identifier, credential.Hash);
    }

    private async Task<Result<LogInOutcome>> RequestVerificationAsync(
        Principal principal, Domain.Owners.Owner owner, EmailIdentifier identifier, Instant now, CancellationToken cancellationToken)
    {
        // A send budget that is spent only means no new code goes out: the owner is still told to verify, with the code they hold.
        var issued = await codes.IssueAsync(principal.Id, CodePurpose.EmailVerification, identifier.NormalizedAddress, cancellationToken);
        if (issued.IsSuccess)
        {
            owner.RecordCodeIssued(CodePurpose.EmailVerification, identifier.Address, issued.Value.Plaintext, now);
        }

        owner.RecordLogIn(verificationPending: true, now);
        return LogInOutcome.VerificationPending(principal.Id);
    }

    /// <summary>The owner an address belongs to, what they are made of and the hash of their password.</summary>
    private sealed record Holder(Principal Principal, Domain.Owners.Owner Owner, EmailIdentifier Identifier, string Hash);
}
