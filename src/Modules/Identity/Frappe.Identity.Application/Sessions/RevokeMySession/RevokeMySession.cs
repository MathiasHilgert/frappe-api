using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.RevokeMySession;

/// <summary>
/// Revokes one live session of the caller (S13), the current one included. A session that is not the caller's, that is already over or that does
/// not exist is the one <c>identity.session.not_found</c>, so the caller learns nothing about other principals' sessions (SEC-05).
/// </summary>
/// <param name="PrincipalId">The caller, from the authenticated request.</param>
/// <param name="SessionId">The session to revoke, from the URL; an id that is not one is <see cref="Guid.Empty"/>, which no session has.</param>
public sealed record RevokeMySession(Guid PrincipalId, Guid SessionId) : ICommand<Unit>;
