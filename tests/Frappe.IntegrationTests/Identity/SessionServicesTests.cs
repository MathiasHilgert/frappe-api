using System.Security.Claims;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Infrastructure.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>What later handlers build on: the current principal, the owner-self policy and the session cookie.</summary>
public sealed class SessionServicesTests(ApiFixture fixture)
{
    [Fact]
    public async Task AuthenticatedRequest_ExposesTheCurrentPrincipal_AndSatisfiesOwnerSelf()
    {
        var (principal, session, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = await AuthenticateAsync(scope.ServiceProvider, $"Bearer {token}");
        Enter(scope.ServiceProvider, context);

        var current = scope.ServiceProvider.GetRequiredService<ICurrentPrincipal>().Session.ShouldNotBeNull();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        current.ShouldBe(new AuthenticatedSession(principal.Id, session.Id, PrincipalType.Owner));
        (await authorization.AuthorizeAsync(context.User, IdentityPolicies.OwnerSelf)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AnonymousRequest_HasNoCurrentPrincipal_AndFailsOwnerSelf()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = await AuthenticateAsync(scope.ServiceProvider, authorization: null);
        Enter(scope.ServiceProvider, context);

        scope.ServiceProvider.GetRequiredService<ICurrentPrincipal>().Session.ShouldBeNull();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        (await authorization.AuthorizeAsync(context.User, IdentityPolicies.OwnerSelf)).Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task SessionCookie_IsIssuedHttpOnlySecureLax()
    {
        var header = await SetCookieAsync(c => c.Issue("opaque-token", new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));

        header.ShouldStartWith("__host-frappe_session=opaque-token");
        header.ShouldContain("expires=sun, 01 feb 2026 00:00:00 gmt");
        ShouldBeLockedDown(header);
    }

    [Fact]
    public async Task SessionCookie_IsClearedWithTheSameAttributes()
    {
        var header = await SetCookieAsync(c => c.Clear());

        header.ShouldStartWith($"{ApiFixture.SessionCookieName}=;");
        header.ShouldContain("expires=thu, 01 jan 1970");
        ShouldBeLockedDown(header);
    }

    [Theory]
    [InlineData("", "/", null)]
    [InlineData("bad name;", "/", null)]
    [InlineData("frappe_session", "relative", null)]
    [InlineData("__Host-frappe_session", "/app", null)]
    [InlineData("__Host-frappe_session", "/", "example.com")]
    public async Task InvalidCookieOptions_StopTheHostFromStarting(string name, string path, string? domain)
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            fixture.CreateHostAsync(services => services.Configure<SessionCookieOptions>(o =>
            {
                o.Name = name;
                o.Path = path;
                o.Domain = domain;
            })));

        exception.Message.ShouldContain($"'{SessionCookieOptions.Section}");
    }

    private async Task<string> SetCookieAsync(Action<ISessionCookie> act)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Enter(scope.ServiceProvider, context);

        act(scope.ServiceProvider.GetRequiredService<ISessionCookie>());

        return context.Response.Headers.SetCookie.ShouldHaveSingleItem().ShouldNotBeNull().ToLowerInvariant();
    }

    private static void ShouldBeLockedDown(string header)
    {
        header.ShouldContain("path=/");
        header.ShouldContain("secure");
        header.ShouldContain("samesite=lax");
        header.ShouldContain("httponly");
        header.ShouldNotContain("domain=");
    }

    // The accessor is backed by an AsyncLocal, so it must be set by the caller, not inside an awaited helper.
    private static void Enter(IServiceProvider services, HttpContext context) =>
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

    private static async Task<HttpContext> AuthenticateAsync(IServiceProvider services, string? authorization)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        if (authorization is not null)
            context.Request.Headers.Authorization = authorization;

        var result = await context.AuthenticateAsync();
        context.User = result.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        return context;
    }
}
