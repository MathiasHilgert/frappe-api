using System.Net;
using System.Net.Http.Headers;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>CACHE-08 / HTTP-27: must-be-fresh data answers no-cache with a strong ETag and a 304 to a matching If-None-Match.</summary>
public sealed class HttpRevalidationTests
{
    /// <summary>The sample resource the endpoint serves: a name and a version that drives its ETag.</summary>
    private sealed record Calendar(string Name, int Version);

    private static async Task<WebApplication> StartAsync(Func<Calendar> current)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddFrappeHttp().AddErrorMessages(PlatformTestMessages.Assembly);

        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").AnswerHead().NegotiateRequestLanguage();
        v1.MapGet("/fresh", (HttpContext context) =>
        {
            ServedLanguages.Declare(context, [ServedLanguages.LanguageOf(context)]);
            return TypedResults.Ok(current());
        }).NoCache();
        v1.MapGet("/fresh-or-missing", Results<Ok<Calendar>, NotFound> (HttpContext context) =>
            context.Request.Query.ContainsKey("missing") ? TypedResults.NotFound() : TypedResults.Ok(current())).NoCache();
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplication app, string path, EntityTagHeaderValue? ifNoneMatch = null, string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (ifNoneMatch is not null)
        {
            request.Headers.IfNoneMatch.Add(ifNoneMatch);
        }

        if (language is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    [Test]
    public async Task A_must_be_fresh_answer_is_no_cache_with_a_strong_validator_and_never_a_max_age()
    {
        // Arrange
        await using var app = await StartAsync(() => new Calendar("holidays", 1));

        // Act
        using var response = await GetAsync(app, "/v1/fresh");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.NoCache).IsTrue();
            await Assert.That(response.Headers.CacheControl?.MaxAge).IsNull();
            await Assert.That(response.Headers.CacheControl?.Public).IsFalse();
            await Assert.That(response.Headers.ETag?.IsWeak).IsFalse();
            await Assert.That(response.Headers.ETag?.Tag).IsNotNull();
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{\"name\":\"holidays\",\"version\":1}");
        }
    }

    [Test]
    public async Task A_matching_If_None_Match_answers_304_without_a_body_and_a_change_answers_200_with_a_new_tag()
    {
        // Arrange
        var version = 1;
        await using var app = await StartAsync(() => new Calendar("holidays", version));
        using var first = await GetAsync(app, "/v1/fresh");
        var tag = first.Headers.ETag!;

        using var unchanged = await GetAsync(app, "/v1/fresh", tag);
        version = 2;

        // Act
        using var changed = await GetAsync(app, "/v1/fresh", tag);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(unchanged.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
            await Assert.That(await unchanged.Content.ReadAsStringAsync()).IsEmpty();
            await Assert.That(unchanged.Headers.ETag).IsEqualTo(tag);
            await Assert.That(unchanged.Headers.CacheControl?.NoCache).IsTrue();
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(changed.Headers.ETag).IsNotEqualTo(tag);
        }
    }

    [Test]
    public async Task Each_language_is_its_own_representation_with_its_own_tag()
    {
        // Arrange
        await using var app = await StartAsync(() => new Calendar("holidays", 1));
        using var english = await GetAsync(app, "/v1/fresh", language: "en");
        using var spanish = await GetAsync(app, "/v1/fresh", language: "es");

        // Act
        using var spanishWithEnglishTag = await GetAsync(app, "/v1/fresh", english.Headers.ETag, "es");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(spanishWithEnglishTag.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(spanish.Headers.Vary.ToArray()).Contains("Accept-Language");
        }
    }

    [Test]
    public async Task A_problem_keeps_its_own_caching_rules()
    {
        // Arrange
        await using var app = await StartAsync(() => new Calendar("holidays", 1));

        // Act
        using var response = await GetAsync(app, "/v1/fresh-or-missing?missing=1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(response.Headers.ETag).IsNull();
            await Assert.That(response.Headers.CacheControl?.NoCache).IsNotEqualTo(true);
        }
    }
}
