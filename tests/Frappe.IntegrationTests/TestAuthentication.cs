using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.IntegrationTests;

/// <summary>Authenticates every request, standing in for sessions until Identity provides them.</summary>
internal static class TestAuthentication
{
    private const string SchemeName = "Test";

    public static void AuthenticateEveryone(this IServiceCollection services) =>
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = SchemeName;
                options.DefaultAuthenticateScheme = SchemeName;
                options.DefaultChallengeScheme = SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, Handler>(SchemeName, null);

    private sealed class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
