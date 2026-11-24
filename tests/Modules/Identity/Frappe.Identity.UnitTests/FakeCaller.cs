using Frappe.Access.Contracts;

namespace Frappe.Identity.UnitTests;

/// <summary>The caller a handler under test sees: an owner on a session, as the session scheme would fill it on the Api.</summary>
internal static class FakeCaller
{
    public static ICaller Of(Guid principalId, Guid? sessionId = null) => new Caller(principalId, "owner", sessionId, null);
}
