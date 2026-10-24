using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

public static class ResendVerificationCodeHandler
{
    public static async Task<Result> Handle(
        ResendVerificationCode command,
        IPrincipalRepository principals,
        VerificationCodeIssuer codes,
        TimeProvider clock,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Result.Failure(IdentityErrors.OwnerNotFound);
        if (principal.Owner.Status != OwnerStatus.Pending)
            return Result.Failure(IdentityErrors.AccountNotPending);

        var issued = codes.Issue(principal);
        if (issued.IsFailure)
        {
            var window = principal.Codes.First(c => c.Purpose == CodePurpose.EmailVerify).WindowStartedAt;
            return Result.Failure(issued.Errors[0].WithRetryAfter(window + VerificationCode.ResendWindow - clock.GetUtcNow()));
        }

        await principals.SaveAsync(cancellationToken);

        await messages.PublishAsync(issued.Value);
        return Result.Success();
    }
}
