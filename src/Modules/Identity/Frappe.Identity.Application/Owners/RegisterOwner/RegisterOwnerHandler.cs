using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

public static class RegisterOwnerHandler
{
    public static async Task<Result<Guid>> Handle(
        RegisterOwner command,
        IPrincipalRepository principals,
        PasswordPolicy passwordPolicy,
        IPasswordHasher passwordHasher,
        VerificationCodeIssuer codes,
        IAttemptLimiter limiter,
        TimeProvider clock,
        IMessageContext messages,
        CancellationToken cancellationToken)
    {
        // Every attempt counts, successful or not: the limit is what keeps "email taken" from being a free oracle.
        var perIp = await limiter.RecordAsync(AttemptPolicies.RegisterPerIp, command.ClientIp, cancellationToken);
        var perEmail = await limiter.RecordAsync(AttemptPolicies.RegisterPerEmail, command.Email?.Trim() ?? string.Empty, cancellationToken);
        if (!perIp.IsAllowed || !perEmail.IsAllowed)
            return Result.Failure<Guid>(IdentityErrors.TooManyAttempts.WithRetryAfter(
                perIp.RetryAfter > perEmail.RetryAfter ? perIp.RetryAfter : perEmail.RetryAfter));

        var name = PersonName.Create(command.Name);
        var email = Email.Create(command.Email);
        var password = await passwordPolicy.ValidateAsync(command.Password, cancellationToken);
        var invalid = new Notification();
        foreach (var error in name.Errors.Concat(email.Errors).Concat(password.Errors))
            invalid.Add(error);
        if (invalid.HasErrors)
            return Result.Failure<Guid>(invalid);

        // Cheap early answer for the common case, before paying for Argon2id.
        if (await principals.GetByEmailAsync(email.Value, cancellationToken) is not null)
            return Result.Failure<Guid>(IdentityErrors.EmailTaken);

        var principal = Principal.RegisterOwner(name.Value, email.Value, await passwordHasher.HashAsync(password.Value.Value, cancellationToken), command.Locale, clock);
        var issued = codes.Issue(principal).Value; // A new principal has no previous code, so the resend cap cannot apply.
        principals.Add(principal);

        // The unique index on lower(email) is the authority on "taken", so a concurrent sign-up cannot slip through.
        var saved = await principals.SaveClaimingEmailAsync(cancellationToken);
        if (saved.IsFailure)
            return Result.Failure<Guid>(saved.Errors[0]);

        await messages.PublishAsync(issued);
        return Result.Success(principal.Id);
    }
}
