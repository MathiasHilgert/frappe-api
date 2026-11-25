using Frappe.Access.Contracts;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Sessions.StepUp;

/// <summary>
/// Handles <see cref="StepUp"/>. The password goes through <see cref="PasswordRecheck"/> (throttled per principal, fail closed), never through
/// <see cref="PasswordCheck"/> alone, so a stolen session cannot guess it. Refusals are counted here, since nothing commits to count them; a
/// step-up that succeeds is counted once its unit commits.
/// </summary>
internal sealed class StepUpHandler(
    ISessions sessions,
    IPrincipals principals,
    IPasswordCredentials credentials,
    PasswordRecheck recheck,
    SessionsMetrics metrics,
    TimeProvider time,
    ICaller caller) : ICommandHandler<StepUp, SteppedUp>
{
    public async Task<Result<SteppedUp>> HandleAsync(StepUp command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var session = await sessions.FindAsync(caller.RequiredSessionId, cancellationToken);
        var principal = session is null || session.PrincipalId != caller.RequiredPrincipalId
            ? null
            : await principals.FindAsync(caller.RequiredPrincipalId, cancellationToken);
        if (session is null || principal is null)
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        var rechecked = await RecheckAsync(principal.Id, command.Password, cancellationToken);
        if (!rechecked.IsSuccess)
        {
            return Result.Failure<SteppedUp>(rechecked.Errors);
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        return session.StepUp(now) ? new SteppedUp(session.StepUpUntil!.Value) : new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
    }

    /// <summary>Asks the password again; counts the refusals, which nothing commits to count.</summary>
    private async Task<Result<Unit>> RecheckAsync(Guid principalId, string? password, CancellationToken cancellationToken)
    {
        var credential = await credentials.FindAsync(principalId, cancellationToken);
        var stored = credential is null ? null : new PasswordHash(credential.Hash);
        var matched = await recheck.MatchesAsync(principalId, stored, password, cancellationToken);
        if (!matched.IsSuccess)
        {
            if (matched.Errors.Any(error => error is RateLimitedError))
            {
                metrics.SteppedUp(StepUpOutcome.Throttled);
            }

            return Result.Failure<Unit>(matched.Errors);
        }

        if (!matched.Value || credential is null)
        {
            metrics.SteppedUp(StepUpOutcome.Incorrect);
            return Error.RuleViolation(IdentityErrorCodes.PasswordIncorrect);
        }

        return Unit.Value;
    }
}
