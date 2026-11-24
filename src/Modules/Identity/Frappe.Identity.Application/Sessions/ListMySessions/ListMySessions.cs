using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>
/// One page of the caller's live sessions (S13), newest opened first. A session that was revoked, ended or expired is not listed.
/// </summary>
/// <param name="PrincipalId">The caller, from the authenticated request, never from the URL (HTTP-08).</param>
/// <param name="CurrentSessionId">The caller's current session, from the authenticated request: the one the page marks as current.</param>
/// <param name="Limit">How many sessions the page holds at most; already bound by the endpoint (HTTP-16).</param>
/// <param name="After">Where the previous page ended; none for the first page.</param>
public sealed record ListMySessions(Guid PrincipalId, Guid CurrentSessionId, int Limit, SessionPosition? After) : IQuery<MySessionsPage>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;
}
