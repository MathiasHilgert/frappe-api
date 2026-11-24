using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.LogOut;

/// <summary>
/// Ends the session the caller is using (S9): it is revoked for <c>logout</c>, so its token stops working at once. Logging out of a session
/// that is gone, revoked or not the caller's own succeeds and changes nothing (the caller's end state is the same), so it reveals nothing.
/// </summary>
/// <param name="PrincipalId">The caller, from the authenticated request.</param>
/// <param name="SessionId">The caller's current session, from the authenticated request.</param>
public sealed record LogOut(Guid PrincipalId, Guid SessionId) : ICommand<Unit>;
