using System.Net;
using System.Text.Json;
using Alba;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Frappe.IntegrationTests.Identity;

/// <summary>Identity problems answer in the request locale, through ToProblem and through the endpoint-specific helpers alike.</summary>
public sealed class IdentityErrorLocalizationTests(ApiFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData("pt-BR", "Frappé ainda não fala este idioma.")]
    [InlineData("es-AR", "Frappé todavía no habla este idioma.")]
    [InlineData(null, "Frappé does not speak this language yet.")]
    public async Task PatchMe_UnsupportedLocale_AnswersInTheRequestLocale(string? acceptLanguage, string expected)
    {
        var (_, _, token) = await SessionSeeder.SeedAsync(fixture.Host);

        var result = await fixture.Host.Scenario(s =>
        {
            s.Patch.Json(new { locale = "de" }).ToUrl("/v1/identity/me");
            s.WithRequestHeader("Authorization", $"Bearer {token}");
            if (acceptLanguage is not null)
                s.WithRequestHeader("Accept-Language", acceptLanguage);
            s.StatusCodeShouldBe(422);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().ShouldBe(expected);
        body.GetProperty("errors")[0].GetProperty("message").GetString().ShouldBe(expected);
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe("identity.locale.unsupported");
    }

    [Theory]
    [InlineData("pt-BR", "O e-mail ou a senha não estão corretos.")]
    [InlineData("es-AR", "El email o la contraseña no son correctos.")]
    [InlineData(null, "The email or password is not correct.")]
    public async Task LogIn_BadCredentials_DetailIsInTheRequestLocale(string? acceptLanguage, string expected)
    {
        var result = await fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.Post.Json(new { email = OwnerSeeder.NewEmail(), password = "not the right password" }).ToUrl("/v1/identity/sessions");
            if (acceptLanguage is not null)
                s.WithRequestHeader("Accept-Language", acceptLanguage);
            s.StatusCodeShouldBe(401);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().ShouldBe(expected);
        body.GetProperty("code").GetString().ShouldBe("identity.credentials.invalid");
        body.GetProperty("type").GetString().ShouldBe("urn:frappe:problem:identity.credentials.invalid");
    }

    [Theory]
    [InlineData("pt-BR", "Já existe uma conta com este e-mail.")]
    [InlineData("es-AR", "Ya existe una cuenta con este email.")]
    [InlineData(null, "An account with this email already exists.")]
    public async Task Register_EmailTaken_TitleIsInTheRequestLocale(string? acceptLanguage, string expected)
    {
        var email = OwnerSeeder.NewEmail();
        await OwnerSeeder.SeedAsync(fixture.Host, email);

        var result = await fixture.Host.Scenario(s =>
        {
            s.ConfigureHttpContext(c => c.Connection.RemoteIpAddress = NewIp());
            s.Post.Json(new { name = "Ana", email, password = "a long enough passphrase" }).ToUrl("/v1/identity/owners");
            if (acceptLanguage is not null)
                s.WithRequestHeader("Accept-Language", acceptLanguage);
            s.StatusCodeShouldBe(409);
        });

        var body = await result.ReadAsJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().ShouldBe(expected);
        body.GetProperty("code").GetString().ShouldBe("email_taken");
        body.GetProperty("type").GetString().ShouldBe("urn:frappe:problem:email_taken");
    }

    private static IPAddress NewIp() => new(Random.Shared.NextInt64(0x0A000001, 0x0AFFFFFE));
}
