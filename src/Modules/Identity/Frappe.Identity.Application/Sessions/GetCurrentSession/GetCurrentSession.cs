using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.GetCurrentSession;

/// <summary>
/// The session a request is using, as its holder sees it (S8): when it idles out, when it ends and how long its step-up still counts. A
/// step-up that has run out is reported as none.
/// </summary>
/// <param name="SessionId">The caller's current session, from the authenticated request.</param>
public sealed record GetCurrentSession(Guid SessionId) : IQuery<CurrentSessionView>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;
}
