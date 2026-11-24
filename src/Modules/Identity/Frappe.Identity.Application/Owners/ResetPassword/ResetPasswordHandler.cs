using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.ResetPassword;

/// <summary>
/// Handles <see cref="ResetPassword"/>. The new password is judged first, so a weak one costs the code no guess and the answer to a made-up id
/// and to a real one is the same; then the code is redeemed by its id (the limiter takes its slot before anything is looked up). Setting the
/// password, rotating the stamp and revoking every session are one unit with the redemption: a failure leaves the code as it was.
/// </summary>
internal sealed class ResetPasswordHandler(
    CodeRedeemer redeemer,
    PasswordPolicy policy,
    IPasswordHasher hasher,
    IPrincipals principals,
    IEmailIdentifiers identifiers,
    IPasswordCredentials credentials,
    CredentialChange change,
    TimeProvider time) : ICommandHandler<ResetPassword, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResetPassword command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var accepted = await policy.AcceptAsync(command.NewPassword, cancellationToken);
        if (!accepted.IsSuccess)
        {
            return Result.Failure<Unit>(accepted.Errors);
        }

        var redeemed = await redeemer.RedeemByIdAsync(command.ResetId, CodePurpose.PasswordReset, command.Code, cancellationToken);
        if (!redeemed.IsSuccess)
        {
            return Result.Failure<Unit>(redeemed.Errors);
        }

        var principal = await principals.FindAsync(redeemed.Value.PrincipalId, cancellationToken);
        var identifier = principal is { Kind: PrincipalKind.Owner, Status: PrincipalStatus.Active }
            ? await identifiers.FindOfAsync(principal.Id, cancellationToken)
            : null;
        var credential = identifier is null ? null : await credentials.FindAsync(identifier.PrincipalId, cancellationToken);

        // The code was sent to an address the owner no longer has: it proves nothing about whoever holds the account now.
        if (principal is null || identifier is null || credential is null || identifier.NormalizedAddress != redeemed.Value.Target)
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        credential.Reset(hasher.Hash(accepted.Value), Instant.FromDateTimeOffset(time.GetUtcNow()));
        await change.ApplyAsync(principal, RevocationReason.PasswordReset, keptSessionId: null, cancellationToken);
        return Unit.Value;
    }
}
