using Frappe.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Http.Sessions;

/// <summary>Registers how Identity proves a request's session: the <see cref="Scheme"/> scheme and the policy routes ask for.</summary>
internal static class SessionAuthentication
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the scheme and <see cref="SessionPolicy.Name"/>. The scheme is the default of nothing (SEC-01): the framework would make a lone
        /// scheme the default one and read the session of every request, anonymous routes included, so that is switched off and only a route
        /// that calls <c>RequireSession()</c> authenticates.
        /// </summary>
        public IServiceCollection AddSessionAuthentication()
        {
            ArgumentNullException.ThrowIfNull(services);
            AppContext.SetSwitch(SuppressAutoDefaultScheme, true);
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
            services.AddAuthorizationBuilder()
                .AddPolicy(SessionPolicy.Name, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
            return services;
        }
    }

    /// <summary>The name of the authentication scheme that reads the session token.</summary>
    public const string Scheme = "Session";

    /// <summary>The switch that stops the framework from making a lone scheme the default one.</summary>
    private const string SuppressAutoDefaultScheme = "Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme";
}
