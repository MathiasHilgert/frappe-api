using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Owners;

/// <summary>Issues a fresh email-verification code: the principal keeps its hash, the message carries the code to the mailer.</summary>
public sealed class VerificationCodeIssuer(ISecretGenerator generator, ISecretHasher hasher, TimeProvider clock)
{
    public Result<OwnerVerificationCodeIssued> Issue(Principal principal)
    {
        var code = generator.NewVerificationCode();
        var issued = principal.IssueCode(CodePurpose.EmailVerify, hasher.Hash(code), clock);
        return issued.IsFailure
            ? Result.Failure<OwnerVerificationCodeIssued>(issued.Errors[0])
            : Result.Success(new OwnerVerificationCodeIssued(
                principal.Id, principal.Owner.Name.Value, principal.Owner.Email.Value, code, issued.Value.ExpiresAt,
                principal.Owner.Locale.Value));
    }
}
