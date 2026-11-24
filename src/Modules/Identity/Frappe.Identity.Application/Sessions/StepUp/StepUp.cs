using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.StepUp;

/// <summary>
/// Steps up the session the caller is using (S14): the password is asked again (throttled per principal, 422 <c>identity.password.incorrect</c>
/// when wrong) and, when it is right, the session may do sensitive actions for five minutes from now. It carries a password, so it prints none
/// (DOM-06).
/// </summary>
/// <param name="Password">The password the caller has, as typed.</param>
public sealed record StepUp(string? Password) : ICommand<SteppedUp>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;

    public override string ToString() => "StepUp { the password is hidden }";
}
