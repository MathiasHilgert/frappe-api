using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>What a client is told when it spends the anonymous limit: a <c>429</c> Problem Details with the wait, in its language.</summary>
public sealed class RateLimitResponseTests
{
    private static async Task<WebApplication> StartAsync(int permitLimit)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:RateLimit:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Http:RateLimit:Window"] = "00:10:00",
        });
        builder.Services
            .AddFrappeHttp()
            .AddErrorMessages(PlatformTestMessages.Assembly);

        var app = builder.Build();
        app.UseFrappeHttp();
        app.MapGroup("/v1")
            .NegotiateRequestLanguage()
            .RequireRateLimiting(HttpRegistration.AnonymousPolicy)
            .MapGet("/language", (Microsoft.AspNetCore.Http.HttpContext context) => context.Features.Get<RequestLanguage>()!.Tag.Value);
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/language");
        if (acceptLanguage is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    [Test]
    public async Task Requests_beyond_the_limit_get_429_with_retry_after()
    {
        // Arrange
        await using var app = await StartAsync(permitLimit: 2);

        using var first = await GetAsync(app, "es");
        using var second = await GetAsync(app, "es");

        // Act
        using var third = await GetAsync(app, acceptLanguage: null);
        var problem = await ProblemAsync(third);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(third.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(third.Headers.RetryAfter?.Delta).IsNotNull();
            await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:too-many-requests");
            await Assert.That(problem.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RateLimitedCode);
            await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(429);
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("Too many requests.");
            await Assert.That(third.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["en"]);
        }
    }

    [Test]
    public async Task A_429_follows_Accept_Language_with_a_stable_title_and_the_wait_in_its_detail()
    {
        // Arrange
        await using var app = await StartAsync(permitLimit: 1);

        using var first = await GetAsync(app, "es-AR");

        // Act
        using var limited = await GetAsync(app, "fr, es-AR;q=0.8");
        var problem = await ProblemAsync(limited);

        // Assert
        var seconds = (int)limited.Headers.RetryAfter!.Delta!.Value.TotalSeconds;
        var error = problem.GetProperty("errors").EnumerateArray().Single();
        using (Assert.Multiple())
        {
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(seconds).IsGreaterThan(0);
            await Assert.That(limited.Headers.Vary.ToArray()).Contains("Accept-Language");
            await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo("Demasiadas solicitudes.");
            await Assert.That(problem.GetProperty("detail").GetString())
                .IsEqualTo($"Usted envió más solicitudes de las permitidas. Vuelva a intentarlo en {seconds} segundos.");
            await Assert.That(problem.TryGetProperty("parameters", out _)).IsFalse();
            await Assert.That(error.GetProperty("parameters").GetProperty("retryAfter").GetInt32()).IsEqualTo(seconds);
            await Assert.That(error.GetProperty("code").GetString()).IsEqualTo(HttpRegistration.RateLimitedCode);
            await Assert.That(error.GetProperty("message").GetString()).IsEqualTo(problem.GetProperty("detail").GetString());
            await Assert.That(limited.Content.Headers.ContentLanguage.ToArray()).IsEquivalentTo(["es", "es-AR"]);
        }
    }
}
