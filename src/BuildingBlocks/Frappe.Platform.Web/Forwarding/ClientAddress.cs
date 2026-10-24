using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Forwarding;

public static class ClientAddress
{
    /// <summary>
    /// The caller's IP as a string (rate-limit key, audit), or <c>unknown</c> when the transport has none. Already the
    /// forwarded address when the request came through a trusted proxy.
    /// </summary>
    public static string ClientIp(this HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
