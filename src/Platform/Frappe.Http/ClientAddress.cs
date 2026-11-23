using Microsoft.AspNetCore.Http;

namespace Frappe.Http;

/// <summary>
/// The client a request counts against: its remote address (behind a known proxy, the one the forwarded headers name),
/// or <see cref="Unknown"/> when there is none. Every per-client limit partitions by it, so they all agree on who a
/// client is.
/// </summary>
public static class ClientAddress
{
    /// <summary>The client of a request without a remote address.</summary>
    public const string Unknown = "unknown";

    public static string Of(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? Unknown;
    }
}
