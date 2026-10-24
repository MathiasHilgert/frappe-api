using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Web.Security;

public static class SecurityRegistration
{
    public const string UnauthenticatedScheme = "Unauthenticated";

    /// <summary>
    /// Deny by default: every endpoint requires an authenticated user unless it opts out with AllowAnonymous.
    /// A challenge or forbid answers the one problem shape (<see cref="PlatformErrors"/>).
    /// The scheme registered here authenticates nobody; a module that issues sessions replaces it as the default.
    /// </summary>
    public static IServiceCollection AddDenyByDefaultSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(UnauthenticatedScheme)
            .AddScheme<AuthenticationSchemeOptions, UnauthenticatedHandler>(UnauthenticatedScheme, null);

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // A 401/403 from authorization answers the same problem shape as an endpoint failure.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();

        return services;
    }

    private sealed class UnauthenticatedHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
