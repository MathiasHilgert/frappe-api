using Frappe.Http.TrustedProxies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>Which proxies the client's address and scheme are believed from.</summary>
public static class ForwardedHeadersRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> are trusted only from the proxies and networks in
        /// <c>Http:ForwardedHeaders:KnownProxies</c> and <c>KnownNetworks</c> (comma separated, <see cref="TrustedProxyOptions"/>),
        /// validated at startup. With none configured the headers are ignored, so a client can never spoof its address to
        /// dodge the rate limiter.
        /// </summary>
        public IServiceCollection AddFrappeForwardedHeaders()
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddOptions<TrustedProxyOptions>().BindConfiguration(TrustedProxyOptions.Section).ValidateOnStart();
            services.AddSingleton<IValidateOptions<TrustedProxyOptions>, TrustedProxyOptionsValidator>();
            services.AddOptions<ForwardedHeadersOptions>()
                .Configure<IOptions<TrustedProxyOptions>>((options, trusted) => Configure(options, trusted.Value));
            return services;
        }
    }

    private static void Configure(ForwardedHeadersOptions options, TrustedProxyOptions trusted)
    {
        var proxies = trusted.Proxies();
        var networks = trusted.Networks();
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        // ASP.NET Core trusts every sender when both lists are empty, so with nothing configured the headers are
        // not processed at all.
        if (proxies.Length == 0 && networks.Length == 0)
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Walk the whole chain of trusted proxies: the walk still stops at the first hop that is not trusted, so a
        // client cannot forge its way past it, and two proxies in a row never put every client in one bucket.
        options.ForwardLimit = null;
        foreach (var proxy in proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (var network in networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}
