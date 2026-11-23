using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The OpenAPI document the platform's transformers produce: the API's info, its Problem schema and what each endpoint convention
/// declares.</summary>
public sealed class OpenApiDocumentTests
{
    /// <summary>The body of the test endpoints.</summary>
    private sealed record Order(string Item);

    private static async Task<WebApplication> StartAsync(IDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        builder.Services
            .AddFrappeHttp(new AnonymousRateLimit(100, TimeSpan.FromMinutes(10)))
            .AddErrorMessages(PlatformTestMessages.Assembly)
            .AddFrappeOpenApi();
        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapOpenApi();
        var v1 = app.MapGroup("/v1").NegotiateRequestLanguage();
        v1.MapGet("/orders", () => TypedResults.Ok(new Order("tea"))).CacheFor(TimeSpan.FromMinutes(5));
        v1.MapPost("/orders", (Order order) => TypedResults.Created("/v1/orders/1", order)).AcceptsIdempotencyKey();
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonElement> DocumentAsync(WebApplication app)
    {
        using var document = JsonDocument.Parse(await app.GetTestClient().GetStringAsync("/openapi/v1.json"));
        return document.RootElement.Clone();
    }

    [Test]
    public async Task The_info_is_what_the_host_configured_and_the_problem_every_error_answers_is_one_schema()
    {
        // Arrange
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["Http:OpenApi:Title"] = "Orders API",
            ["Http:OpenApi:Version"] = "2.1.0",
            ["Http:OpenApi:Description"] = "Orders for everyone.",
        });

        // Act
        var document = await DocumentAsync(app);

        // Assert
        var info = document.GetProperty("info");
        var schemas = document.GetProperty("components").GetProperty("schemas");
        using (Assert.Multiple())
        {
            await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("Orders API");
            await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("2.1.0");
            await Assert.That(info.GetProperty("description").GetString()).IsEqualTo("Orders for everyone.");
            await Assert.That(schemas.TryGetProperty("Problem", out _)).IsTrue();
            await Assert.That(schemas.TryGetProperty("ProblemError", out _)).IsTrue();
            await Assert.That(schemas.TryGetProperty("ProblemDetails", out _)).IsFalse();
        }
    }

    [Test]
    public async Task Without_configuration_the_document_has_a_neutral_title_and_version()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        var info = (await DocumentAsync(app)).GetProperty("info");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(info.GetProperty("title").GetString()).IsEqualTo("API");
            await Assert.That(info.GetProperty("version").GetString()).IsEqualTo("1.0.0");
        }
    }

    [Test]
    public async Task A_blank_title_fails_at_startup_naming_the_key()
    {
        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(
            async () => await StartAsync(new Dictionary<string, string?> { ["Http:OpenApi:Title"] = " " }));

        // Assert
        await Assert.That(failure!.Message).Contains("Configuration 'Http:OpenApi:Title' is required.");
    }

    [Test]
    public async Task Each_endpoint_convention_declares_its_headers_and_every_operation_its_problems()
    {
        // Arrange
        await using var app = await StartAsync();

        // Act
        var paths = (await DocumentAsync(app)).GetProperty("paths");
        var get = paths.GetProperty("/v1/orders").GetProperty("get");
        var post = paths.GetProperty("/v1/orders").GetProperty("post");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(get.GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("ETag", out _)).IsTrue();
            await Assert.That(get.GetProperty("responses").TryGetProperty("304", out _)).IsTrue();
            await Assert.That(get.GetProperty("parameters").EnumerateArray().Select(p =>
                p.GetProperty("name").GetString())).Contains("Accept-Language");
            await Assert.That(post.GetProperty("parameters").EnumerateArray().Select(p =>
                p.GetProperty("name").GetString())).Contains("Idempotency-Key");
            await Assert.That(post.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _)).IsTrue();
            await Assert.That(post.GetProperty("responses").GetProperty("409").GetProperty("description").GetString())
                .Contains("platform.persistence.commit_conflict");
            await Assert.That(post.GetProperty("responses").TryGetProperty("415", out _)).IsTrue();
            await Assert.That(get.GetProperty("responses").TryGetProperty("500", out _)).IsTrue();
            await Assert.That(get.GetProperty("responses").TryGetProperty("503", out _)).IsTrue();
        }
    }
}
