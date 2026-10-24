using Frappe.Identity.Application.Sessions;
using Frappe.Platform.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Sessions;

internal static class SessionAuthenticationRegistration
{
    /// <summary>Makes owner sessions the default scheme, replacing the platform's authenticate-nobody placeholder.</summary>
    public static IServiceCollection AddSessionAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<SessionCookieOptions>, SessionCookieOptionsValidator>();
        services.AddOptions<SessionCookieOptions>()
            .BindConfiguration(SessionCookieOptions.SectionName)
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
