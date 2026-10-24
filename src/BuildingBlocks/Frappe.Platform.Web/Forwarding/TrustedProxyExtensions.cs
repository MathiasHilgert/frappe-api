using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Web.Forwarding;

/// <summary>
/// Behind a reverse proxy the socket address is the proxy's. <c>X-Forwarded-For</c>/<c>-Proto</c> are honoured only
/// when the request comes from a trusted proxy: loopback, plus every address in <c>ForwardedHeaders:KnownProxies</c>
/// and every CIDR range in <c>ForwardedHeaders:KnownNetworks</c>. Anyone else's headers are ignored, so a client
/// cannot spoof its address (and dodge per-IP rate limits) by sending the header itself.
/// </summary>
public static class TrustedProxyExtensions
{
    public const string Section = "ForwardedHeaders";

    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var proxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var networks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];

        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in proxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in networks)
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });
    }

    /// <summary>Place first: everything after it (correlation, rate limits, logs) must see the real client.</summary>
    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app) => app.UseForwardedHeaders();
}
