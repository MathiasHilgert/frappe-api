using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HTTP-25: an enumerated value is <c>{code, name}</c>, the name written in the request language.</summary>
public sealed class EnumeratedValuesTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddFrappeHttp().AddErrorMessages(PlatformTestMessages.Assembly);
        builder.Services.Configure<ErrorMessageOptions>(options => options.Add("test", new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new() { ["test.kind.holiday"] = "Holiday", ["test.kind.bridge"] = "Bridge day", ["test.only_english.value"] = "English only" },
            ["es"] = new() { ["test.kind.holiday"] = "Feriado" },
        }));

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapGet("/kind", (HttpContext context) => EnumeratedValues.Of(context, "test.kind", "holiday"));
        v1.MapGet("/kinds", (HttpContext context) => EnumeratedValues.Of(context, "test.kind", ["holiday", "bridge"]));
        v1.MapGet("/english-only", (HttpContext context) => EnumeratedValues.Of(context, "test.only_english", "value"));
        v1.MapGet("/unnamed", (HttpContext context) => EnumeratedValues.Of(context, "test.kind", "unknown"));
        await app.StartAsync();
        return app;
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> GetAsync(WebApplication app, string path, string? language)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        var response = await app.GetTestClient().SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? default : JsonElement.Parse(text));
    }

    [Test]
    [Arguments("en", "Holiday", "en")]
    [Arguments("es", "Feriado", "es")]
    [Arguments("es-AR", "Feriado", "es")]
    [Arguments("pt-BR", "Holiday", "en")]
    [Arguments(null, "Holiday", "en")]
    public async Task The_name_follows_the_request_language_with_the_platform_fallback_and_the_response_declares_it(string? language, string name, string declared)
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        var (response, body) = await GetAsync(app, "/v1/kind", language);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body.GetProperty("code").GetString()).IsEqualTo("holiday");
            await Assert.That(body.GetProperty("name").GetString()).IsEqualTo(name);
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo([declared]);
        }
    }

    [Test]
    public async Task A_list_of_values_is_a_list_of_code_and_name_and_declares_every_language_it_used()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        var (response, body) = await GetAsync(app, "/v1/kinds", "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(body.GetArrayLength()).IsEqualTo(2);
            await Assert.That(body[0].GetProperty("name").GetString()).IsEqualTo("Feriado");
            await Assert.That(body[1].GetProperty("code").GetString()).IsEqualTo("bridge");
            await Assert.That(body[1].GetProperty("name").GetString()).IsEqualTo("Bridge day");
            await Assert.That(response.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["en", "es"]);
        }
    }

    [Test]
    public async Task A_value_without_any_name_is_a_server_error_never_a_bare_code()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        var (response, _) = await GetAsync(app, "/v1/unnamed", "en");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
    }
}
