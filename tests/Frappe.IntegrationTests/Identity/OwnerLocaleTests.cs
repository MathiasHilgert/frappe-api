using System.Net;
using System.Text.Json;
using Alba;
using Frappe.Identity.Contracts;
using Frappe.Identity.Contracts.PasswordResets;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Wolverine.Tracking;

namespace Frappe.IntegrationTests.Identity;

/// <summary>The owner's stored locale: set at sign-up, read by GET /me, changed by PATCH /me, preferred by every response.</summary>
public sealed class OwnerLocaleTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Me = "/v1/identity/me";

    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("es-mx", "es-MX")]
    [InlineData("es-CL,en;q=0.5", "es-AR")]
    [InlineData("de", "en")]
    [InlineData(null, "en")]
    public async Task Register_StoresTheLocaleNegotiatedFromAcceptLanguage(string? acceptLanguage, string expected)
    {
        IScenarioResult result = null!;
        var tracked = await fixture.Host.ExecuteAndWaitAsync(async () => result = await fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.Post.Json(new { name = "Ana", email = OwnerSeeder.NewEmail(), password = "a long enough passphrase" }).ToUrl("/v1/identity/owners");
            if (acceptLanguage is not null)
                s.WithRequestHeader("Accept-Language", acceptLanguage);
            s.StatusCodeShouldBe(201);
        }));

        var id = (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await SessionSeeder.LoadAsync(fixture.Host, id)).Owner.Locale.Value.ShouldBe(expected);
        tracked.Sent.MessagesOf<OwnerVerificationCodeIssued>().ShouldHaveSingleItem().Locale.ShouldBe(expected);
    }

    [Fact]
    public async Task GetMe_ReturnsTheStoredLocale_AndAnswersInIt()
    {
        var (owner, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await PatchAsync(token, new { locale = "pt-BR" }, 200);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Me);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(200);
        });

        (await result.ReadAsJsonAsync<JsonElement>()).GetProperty("locale").GetString().ShouldBe("pt-BR");
        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("pt-BR");
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Owner.Locale.Value.ShouldBe("pt-BR");
    }

    [Fact]
    public async Task AnExplicitAcceptLanguage_OverridesTheStoredPreference()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await PatchAsync(token, new { locale = "pt-BR" }, 200);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Get.Url(Me);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.WithRequestHeader("Accept-Language", "es-AR");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.Headers.ContentLanguage.ToString().ShouldBe("es-AR");
    }

    [Fact]
    public async Task PatchMe_CanonicalisesTheTag_AndReturnsTheProfile()
    {
        var (owner, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        var body = await (await PatchAsync(token, new { locale = "es_mx" }, 200)).ReadAsJsonAsync<JsonElement>();

        body.GetProperty("locale").GetString().ShouldBe("es-MX");
        body.GetProperty("id").GetGuid().ShouldBe(owner.Id);
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Owner.Locale.Value.ShouldBe("es-MX");
    }

    [Theory]
    [InlineData("not a tag!", "locale.invalid")]
    [InlineData("", "locale.invalid")]
    [InlineData("de-DE", "identity.locale.unsupported")]
    public async Task PatchMe_RejectsBadLocales_With422_AndKeepsTheStoredOne(string locale, string code)
    {
        var (owner, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        var body = await (await PatchAsync(token, new { locale }, 422)).ReadAsJsonAsync<JsonElement>();

        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe(code);
        body.GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("locale");
        (await SessionSeeder.LoadAsync(fixture.Host, owner.Id)).Owner.Locale.Value.ShouldBe("en");
    }

    [Fact]
    public async Task PasswordResetCode_CarriesTheOwnersLocale()
    {
        var (owner, _, token) = await SessionSeeder.SeedAsync(fixture.Host);
        await PatchAsync(token, new { locale = "es-MX" }, 200);

        var tracked = await fixture.Host.ExecuteAndWaitAsync(() => fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.Post.Json(new { email = owner.Owner.Email.Value }).ToUrl("/v1/identity/password-resets");
            s.StatusCodeShouldBe(202);
        }));

        tracked.Sent.MessagesOf<PasswordResetCodeIssued>().ShouldHaveSingleItem().Locale.ShouldBe("es-MX");
    }

    [Fact]
    public async Task PatchMe_Anonymous_Is401() =>
        await fixture.Host.Scenario(s =>
        {
            s.Patch.Json(new { locale = "en" }).ToUrl(Me);
            s.StatusCodeShouldBe(401);
        });

    [Fact]
    public async Task PatchMe_WithCookie_NeedsTheCsrfHeader()
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        await fixture.Host.Scenario(s =>
        {
            s.Patch.Json(new { locale = "en" }).ToUrl(Me);
            s.WithRequestHeader("Cookie", $"{ApiFixture.SessionCookieName}={token}");
            s.StatusCodeShouldBe(401);
        });
        await fixture.Host.Scenario(s =>
        {
            s.Patch.Json(new { locale = "pt-BR" }).ToUrl(Me);
            s.WithRequestHeader("Cookie", $"{ApiFixture.SessionCookieName}={token}");
            s.WithRequestHeader("X-Requested-With", "frappe");
            s.StatusCodeShouldBe(200);
        });
    }

    private Task<IScenarioResult> PatchAsync(string token, object body, int status) =>
        fixture.Host.Scenario(s =>
        {
            s.Patch.Json(body).ToUrl(Me);
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            s.StatusCodeShouldBe(status);
        });

    private static IPAddress NewIp() => new(Random.Shared.NextInt64(0x0A000001, 0x0AFFFFFE));
}
