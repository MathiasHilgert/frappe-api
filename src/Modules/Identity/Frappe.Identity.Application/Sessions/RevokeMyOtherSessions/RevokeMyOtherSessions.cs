using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;

/// <summary>Revokes every live session of the caller except the one in use (S13); having no other session is not an error.</summary>
/// <param name="PrincipalId">The caller, from the authenticated request.</param>
/// <param name="CurrentSessionId">The caller's current session, from the authenticated request: the one that stays.</param>
public sealed record RevokeMyOtherSessions(Guid PrincipalId, Guid CurrentSessionId) : ICommand<Unit>;
