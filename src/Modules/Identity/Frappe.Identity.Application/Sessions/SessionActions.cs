using Frappe.Access.Contracts;
using Frappe.Identity.Domain.Sessions;

namespace Frappe.Identity.Application.Sessions;

/// <summary>What a caller may do with a <see cref="Session"/>, checked through <see cref="IAuthorizer"/>.</summary>
public static class SessionActions
{
    /// <summary>Ends the session before its time, at its holder's request.</summary>
    public static readonly ResourceAction<Session> Revoke = new("revoke");
}
