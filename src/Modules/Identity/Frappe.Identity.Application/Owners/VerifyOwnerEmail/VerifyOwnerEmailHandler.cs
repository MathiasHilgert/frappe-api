using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Application.Telemetry;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.VerifyOwnerEmail;

/// <summary>
/// Handles <see cref="VerifyOwnerEmail"/>. Redeeming the code, activating the principal, proving the address, verifying the owner and opening
/// the session are one unit: a failure at any step leaves the code as it was. A refusal is counted here, since nothing commits to count it after.
/// </summary>
internal sealed class VerifyOwnerEmailHandler(
    IPrincipals principals,
    IOwners owners,
    IEmailIdentifiers identifiers,
    CodeRedeemer redeemer,
    SessionOpener sessions,
    OwnersMetrics metrics,
    TimeProvider time) : ICommandHandler<VerifyOwnerEmail, OpenedSession>
{
    public async Task<Result<OpenedSession>> HandleAsync(VerifyOwnerEmail command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await VerifyAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            metrics.Verified(result.Errors.Any(error => error.Code == IdentityErrorCodes.AttemptsUnavailable)
                ? VerificationOutcome.Unavailable
                : VerificationOutcome.Invalid);
        }

        return result;
    }

    private async Task<Result<OpenedSession>> VerifyAsync(VerifyOwnerEmail command, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(command.OwnerId, out var id))
        {
            return Invalid();
        }

        var principal = await principals.FindAsync(id, cancellationToken);
        var pending = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Pending };
        var owner = pending ? await owners.FindAsync(id, cancellationToken) : null;
        var identifier = owner is null ? null : await identifiers.FindOfAsync(id, cancellationToken);
        if (principal is null || owner is null || identifier is null)
        {
            return Invalid();
        }

        var redeemed = await redeemer.RedeemAsync(id, CodePurpose.EmailVerification, command.Code, command.ClientAddress, cancellationToken);
        if (!redeemed.IsSuccess)
        {
            return Result.Failure<OpenedSession>(redeemed.Errors);
        }

        if (redeemed.Value.Target != identifier.NormalizedAddress)
        {
            return Invalid();
        }

        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var verified = owner.Verify(now);
        if (!verified.IsSuccess)
        {
            return Result.Failure<OpenedSession>(verified.Errors);
        }

        principal.Activate();
        identifier.Verify(now);
        return sessions.Open(id, principal.CredentialStamp, command.UserAgent, command.ClientAddress);
    }

    private static Result<OpenedSession> Invalid() => Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
}
