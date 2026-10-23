using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Frappe.Platform.Web.Security;

public static class SecurityRegistration
{
    public const string UnauthenticatedScheme = "Unauthenticated";

    /// <summary>
    /// Deny by default: every endpoint requires an authenticated user unless it opts out with AllowAnonymous.
    /// Until sessions exist, the only scheme authenticates nobody, so protected endpoints answer 401.
    /// </summary>
    public static IServiceCollection AddDenyByDefaultSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(UnauthenticatedScheme)
            .AddScheme<AuthenticationSchemeOptions, UnauthenticatedHandler>(UnauthenticatedScheme, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    private sealed class UnauthenticatedHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
