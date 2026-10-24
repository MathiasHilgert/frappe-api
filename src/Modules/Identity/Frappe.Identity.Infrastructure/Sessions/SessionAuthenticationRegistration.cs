using Frappe.Identity.Application.Sessions;
using Frappe.Platform.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Infrastructure.Sessions;

internal static class SessionAuthenticationRegistration
{
    /// <summary>Makes owner sessions the default scheme, replacing the platform's authenticate-nobody placeholder.</summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SessionCookieOptions>()
            .Bind(configuration.GetSection(SessionCookieOptions.Section))
            .Validate(options => options.IsValid(),
                $"'{SessionCookieOptions.Section}' needs a cookie-safe Name and a Path starting with '/'; a __Host- Name needs Path '/' and no Domain.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<SessionAuthenticator>();
        services.AddScoped<ICurrentPrincipal, HttpCurrentPrincipal>();
        services.AddScoped<ISessionCookie, HttpSessionCookie>();
        services.AddScoped<IPreferredLocaleSource, OwnerPreferredLocaleSource>();

        services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy(IdentityPolicies.OwnerSelf, policy => policy
                .AddAuthenticationSchemes(SessionAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(SessionClaimTypes.PrincipalType, "owner"));

        return services;
    }
}
