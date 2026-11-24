using Frappe.Access.Contracts;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.RevokeMySession;

/// <summary>
/// Revokes one live session of the caller (S13, the caller from the session scheme), the current one included. A session that is not the
/// caller's, that is already over or that does not exist is the one <c>identity.session.not_found</c>, so the caller learns nothing about
/// other principals' sessions (SEC-05).
/// </summary>
/// <param name="SessionId">The session to revoke, from the URL; an id that is not one is <see cref="Guid.Empty"/>, which no session has.</param>
public sealed record RevokeMySession(Guid SessionId) : ICommand<Unit>, IGuarded, IActsOn<Session>
{
    public static CallerRule Allowed => Is.Authenticated;
}
