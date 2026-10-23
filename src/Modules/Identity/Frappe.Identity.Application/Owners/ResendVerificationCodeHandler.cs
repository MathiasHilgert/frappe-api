using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

public static class ResendVerificationCodeHandler
{
    public static readonly Error OwnerNotFound = new("identity.owner.not_found", "The owner does not exist.");

    public static async Task<ResendVerificationCodeResult> Handle(
        ResendVerificationCode command,
        IPrincipalRepository principals,
        VerificationCodeIssuer codes,
        TimeProvider clock,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        var principal = await principals.GetByIdAsync(command.PrincipalId, cancellationToken);
        if (principal is null)
            return Failed(OwnerNotFound);
        if (principal.Owner.Status != OwnerStatus.Pending)
            return Failed(IdentityErrors.AccountNotPending);

        var issued = codes.Issue(principal);
        if (issued.IsFailure)
        {
            var window = principal.Codes.First(c => c.Purpose == CodePurpose.EmailVerify).WindowStartedAt;
            return new(Result.Failure(issued.Errors[0]), window + VerificationCode.ResendWindow - clock.GetUtcNow());
        }

        var saved = await principals.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
            return Failed(saved.Errors[0]);

        await messages.PublishAsync(issued.Value);
        return new(Result.Success(), TimeSpan.Zero);
    }

    private static ResendVerificationCodeResult Failed(Error error) => new(Result.Failure(error), TimeSpan.Zero);
}
