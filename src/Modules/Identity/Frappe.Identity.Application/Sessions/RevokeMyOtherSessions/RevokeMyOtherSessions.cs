using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Sessions.RevokeMyOtherSessions;

/// <summary>Revokes every live session of the caller except the one in use (S13); having no other session is not an error.</summary>
public sealed record RevokeMyOtherSessions : ICommand<Unit>, IGuarded
{
    public static CallerRule Allowed => Is.Authenticated;
}
