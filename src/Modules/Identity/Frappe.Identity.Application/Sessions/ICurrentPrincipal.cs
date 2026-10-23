namespace Frappe.Identity.Application.Sessions;

/// <summary>The session behind the current request, so handlers never read transport details themselves.</summary>
public interface ICurrentPrincipal
{
    /// <summary>Null when the request is anonymous.</summary>
    AuthenticatedSession? Session { get; }
}
