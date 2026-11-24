using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.TouchSession;

/// <summary>
/// Records that a session was used and moves its idle expiry on (S8). Authentication dispatches it at most once a minute per session
/// (<see cref="AuthenticateSession.SessionPrincipal.IsDueForTouch"/>). Several requests of one session may race to do it: the ones that
/// lose commit nothing and answer a commit conflict, which is never a failure of the request that dispatched them.
/// </summary>
/// <param name="SessionId">The session that was used.</param>
public sealed record TouchSession(Guid SessionId) : ICommand<Unit>;
