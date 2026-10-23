using Frappe.Identity.Domain.Principals;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Who is calling: the principal and the session the request was authenticated with.</summary>
public sealed record AuthenticatedSession(Guid PrincipalId, Guid SessionId, PrincipalType PrincipalType);
