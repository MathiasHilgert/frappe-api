using Frappe.Identity.Application.Sessions;

namespace Frappe.Identity.Infrastructure.Endpoints;

internal static class CurrentPrincipalExtensions
{
    /// <summary>
    /// The caller's session on an endpoint that requires one. Authorization already refused anonymous callers, so a
    /// missing session here is a wiring bug (the endpoint forgot <c>RequireOwner()</c>), not a client error.
    /// </summary>
    public static AuthenticatedSession RequiredSession(this ICurrentPrincipal current) =>
        current.Session ?? throw new InvalidOperationException(
            "This endpoint needs an authenticated session; add RequireOwner() to it.");
}
