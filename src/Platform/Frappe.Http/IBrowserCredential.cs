using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>
/// A credential the browser attaches on its own (a cookie), as the module that authenticates with it registers it: what the console origin
/// check (<see cref="ConsoleOriginCheck"/>) asks of every unsafe request, so the host names no module's credential.
/// </summary>
public interface IBrowserCredential
{
    /// <summary>Whether <paramref name="httpContext"/>'s request is judged by this credential; it reads only the request's headers.</summary>
    bool IsCarriedBy(HttpContext httpContext);
}
