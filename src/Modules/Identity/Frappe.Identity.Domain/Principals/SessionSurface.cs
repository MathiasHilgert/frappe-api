namespace Frappe.Identity.Domain.Principals;

/// <summary>Where a session was opened: the web console (cookie) or any other client (Bearer).</summary>
public enum SessionSurface
{
    Console,
    Api,
}
