using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.StepUp;

/// <summary>
/// Steps up the session the caller is using (S14): the password is asked again (throttled per principal, 422 <c>identity.password.incorrect</c>
/// when wrong) and, when it is right, the session may do sensitive actions for five minutes from now. It carries a password, so it prints none
/// (DOM-06).
/// </summary>
/// <param name="PrincipalId">The caller, from the authenticated request.</param>
/// <param name="SessionId">The caller's current session, from the authenticated request.</param>
/// <param name="Password">The password the caller has, as typed.</param>
public sealed record StepUp(Guid PrincipalId, Guid SessionId, string? Password) : ICommand<SteppedUp>
{
    public override string ToString() => $"StepUp {{ PrincipalId = {PrincipalId}, SessionId = {SessionId}, the password is hidden }}";
}
