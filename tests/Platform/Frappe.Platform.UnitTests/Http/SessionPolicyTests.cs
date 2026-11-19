using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Frappe.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>
/// The session policy any module guards a route with (<see cref="SessionPolicy.RequireSession{TBuilder}"/>) and the reader
/// of the principal the session proved. The scheme that proves it is supplied by the module that issues sessions; a stand-in plays it here.
/// </summary>
public sealed class SessionPolicyTests
{
    private const string TestScheme = "TestSession";
    private const string PrincipalHeader = "X-Principal";

    /// <summary>Authenticates a request that carries <c>X-Principal</c> with that value as the principal claim; anything else is anonymous.</summary>
    private sealed class HeaderHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(PrincipalHeader, out var value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(SessionPolicy.PrincipalClaim, value.ToString())], TestScheme, SessionPolicy.PrincipalClaim, null);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderHandler>(TestScheme, displayName: null, _ => { });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(SessionPolicy.Name, policy => policy.AddAuthenticationSchemes(TestScheme).RequireAuthenticatedUser());
        var app = builder.Build();
        app.UseAuthorization();
        app.MapGet("/guarded", (HttpContext httpContext) => SessionPolicy.PrincipalIdOf(httpContext.User)?.ToString() ?? "none").RequireSession();
        app.MapGet("/open", () => "open");
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, string? principal = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (principal is not null)
        {
            request.Headers.TryAddWithoutValidation(PrincipalHeader, principal);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    [Test]
    public async Task A_route_that_requires_a_session_refuses_a_request_without_one()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/guarded");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task A_route_that_requires_a_session_serves_a_request_that_proved_one_and_names_its_principal()
    {
        await using var app = await StartAsync();
        var principal = Guid.CreateVersion7();

        using var response = await GetAsync(app, "/guarded", principal.ToString());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(principal.ToString());
    }

    [Test]
    public async Task A_route_without_the_policy_stays_open()
    {
        await using var app = await StartAsync();

        using var response = await GetAsync(app, "/open");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_principal_of_a_user_without_a_valid_principal_claim_is_nothing()
    {
        var noClaim = new ClaimsPrincipal(new ClaimsIdentity());
        var malformed = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionPolicy.PrincipalClaim, "not-a-guid")]));

        await Assert.That(SessionPolicy.PrincipalIdOf(noClaim)).IsNull();
        await Assert.That(SessionPolicy.PrincipalIdOf(malformed)).IsNull();
    }

    [Test]
    public async Task The_principal_of_a_user_that_never_authenticated_is_nothing_even_when_it_carries_a_valid_principal_claim()
    {
        // Arrange
        var claim = new Claim(SessionPolicy.PrincipalClaim, Guid.CreateVersion7().ToString());
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity([claim]));
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity([claim], TestScheme));

        // Act
        var (before, after) = (SessionPolicy.PrincipalIdOf(unauthenticated), SessionPolicy.PrincipalIdOf(authenticated));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before).IsNull();
            await Assert.That(after).IsEqualTo(Guid.Parse(claim.Value));
        }
    }

    [Test]
    public async Task The_principal_claim_and_the_policy_keep_the_names_the_session_scheme_issues()
    {
        await Assert.That(SessionPolicy.Name).IsEqualTo("Session");
        await Assert.That(SessionPolicy.PrincipalClaim).IsEqualTo("sub");
    }
}
