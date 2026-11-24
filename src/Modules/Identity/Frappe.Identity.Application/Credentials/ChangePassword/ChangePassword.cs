using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Credentials.ChangePassword;

/// <summary>
/// Changes the password of the signed-in principal (S12): the current password is asked again (throttled, 422 <c>identity.password.incorrect</c>
/// when wrong), the new one must meet the policy, every other session is revoked and the one the caller is using keeps working. It carries
/// passwords, so it prints none (DOM-06).
/// </summary>
/// <param name="PrincipalId">The caller, from the authenticated request.</param>
/// <param name="SessionId">The caller's current session, from the authenticated request: the one that survives.</param>
/// <param name="CurrentPassword">The password the caller has now, as typed.</param>
/// <param name="NewPassword">The password the caller wants, as typed.</param>
public sealed record ChangePassword(Guid PrincipalId, Guid SessionId, string? CurrentPassword, string? NewPassword) : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;

    public override string ToString() => $"ChangePassword {{ PrincipalId = {PrincipalId}, SessionId = {SessionId}, the passwords are hidden }}";
}
