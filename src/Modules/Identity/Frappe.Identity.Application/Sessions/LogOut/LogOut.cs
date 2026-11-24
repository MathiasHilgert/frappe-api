using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.LogOut;

/// <summary>
/// Ends the session the caller is using (S9): it is revoked for <c>logout</c>, so its token stops working at once. Logging out of a session
/// that is gone, revoked or not the caller's own succeeds and changes nothing (the caller's end state is the same), so it reveals nothing.
/// </summary>
public sealed record LogOut : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;
}
