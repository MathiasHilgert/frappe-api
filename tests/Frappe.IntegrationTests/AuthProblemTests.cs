using System.Security.Claims;
using System.Text.Encodings.Web;
using Frappe.Platform.Localization;
using Frappe.Platform.Web.Correlation;
using Frappe.Platform.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>A challenge (401) or forbid (403) from authorization answers the same problem shape as any endpoint failure.</summary>
public sealed class AuthProblemTests(ApiFixture fixture)
{
    [Fact]
    public async Task AnonymousCaller_Gets401_InTheOneProblemShape_Translated()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/v1/identity/me");
            s.WithRequestHeader("Accept-Language", "es-AR");
            s.StatusCodeShouldBe(401);
        });

        var body = await result.ShouldBeProblemAsync(401, "platform.auth.required");
        body.GetProperty("title").GetString().ShouldBe("Tenés que iniciar sesión.");
    }

    [Fact]
    public async Task UnknownBearerToken_Gets401_InTheOneProblemShape()
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url("/v1/identity/sessions");
            s.WithRequestHeader("Authorization", "Bearer not-a-real-token");
            s.StatusCodeShouldBe(401);
        });

        await result.ShouldBeProblemAsync(401, "platform.auth.required");
    }

    [Fact]
    public async Task AuthenticatedCallerFailingThePolicy_Gets403_InTheOneProblemShape_Translated()
    {
        await using var app = await StartAsync();

        var context = await app.GetTestServer().SendAsync(c =>
        {
            c.Request.Path = "/admin";
            c.Request.Headers.AcceptLanguage = "pt-BR";
        }, TestContext.Current.CancellationToken);

        context.Response.StatusCode.ShouldBe(403);
        context.Response.ContentType.ShouldBe("application/problem+json");
        using var reader = new StreamReader(context.Response.Body);
        var body = System.Text.Json.JsonDocument.Parse(await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).RootElement;
        body.GetProperty("type").GetString().ShouldBe("urn:frappe:problem:platform.auth.forbidden");
        body.GetProperty("code").GetString().ShouldBe("platform.auth.forbidden");
        body.GetProperty("title").GetString().ShouldBe("Você não tem permissão para fazer isso.");
        body.GetProperty("detail").GetString().ShouldBe(body.GetProperty("title").GetString());
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("platform.auth.forbidden");
    }

    // The platform security and problem blocks, with a scheme that signs everyone in and a policy nobody meets.
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformLocalization(new ConfigurationBuilder().Build());
        builder.Services.AddRequestCorrelation();
        builder.Services.AddDenyByDefaultSecurity();
        builder.Services.AddAuthentication("everyone")
            .AddScheme<AuthenticationSchemeOptions, EveryoneHandler>("everyone", null);
        builder.Services.AddAuthorizationBuilder().AddPolicy("admins", p => p.RequireRole("admin"));

        var app = builder.Build();
        app.UseRequestCorrelation();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UsePlatformLocalization();
        app.UseAuthorization();
        app.MapGet("/admin", () => "secret").RequireAuthorization("admins");
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class EveryoneHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "everyone")), "everyone")));
    }
}
