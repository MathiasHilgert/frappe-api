using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Security;

/// <summary>The full password rule: the domain length policy first, then the breach check for valid lengths only.</summary>
public sealed class PasswordPolicy(IBreachedPasswordChecker breaches)
{
    public async Task<Result<Password>> ValidateAsync(string? candidate, CancellationToken cancellationToken)
    {
        var password = Password.Create(candidate);
        if (password.IsFailure)
            return password;

        return await breaches.IsBreachedAsync(password.Value.Value, cancellationToken)
            ? Result.Failure<Password>(IdentityErrors.PasswordBreached)
            : password;
    }
}
