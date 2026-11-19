using System.Net;
using Frappe.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>HEAD answers the headers of GET without content, and the endpoints declare their caching lifetime.</summary>
public sealed class HeadAndNoStoreTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddFrappeHttp();
        var app = builder.Build();
        app.UseFrappeHttp();
        var v1 = app.MapGroup("/v1").AnswerHead();
        v1.MapGet("/text", () => "some text");
        v1.MapGet("/private", () => TypedResults.Ok(new { secret = "personal" })).NoStore();
        v1.MapGet("/cached", () => TypedResults.Ok(new { name = "x" })).CacheFor(TimeSpan.FromMinutes(5));
        v1.MapGet("/limited", (HttpContext context) =>
        {
            HttpCaching.LimitMaxAge(context, TimeSpan.FromSeconds(30));
            HttpCaching.LimitMaxAge(context, TimeSpan.FromSeconds(60));
            return TypedResults.Ok(new { name = "x" });
        }).CacheFor(TimeSpan.FromMinutes(5));
        v1.MapGet("/not-ok", Results<Ok<string>, NotFound> () => TypedResults.NotFound()).CacheFor(TimeSpan.FromMinutes(5));
        app.MapGet("/get-only", () => "x");
        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task A_GET_route_answers_HEAD_with_the_headers_of_GET_and_no_body()
    {
        await using var app = await StartAsync();
        using var get = await app.GetTestClient().GetAsync("/v1/text");

        using var head = await app.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, "/v1/text"));

        using (Assert.Multiple())
        {
            await Assert.That(head.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(head.Content.Headers.ContentType?.ToString()).IsEqualTo(get.Content.Headers.ContentType?.ToString());
            await Assert.That((await head.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_GET_route_that_did_not_opt_in_does_not_answer_HEAD()
    {
        await using var app = await StartAsync();

        using var head = await app.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, "/get-only"));

        await Assert.That(head.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task NoStore_forbids_caches_and_sends_no_validator()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync("/v1/private");

        using (Assert.Multiple())
        {
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(response.Headers.ETag).IsNull();
        }
    }

    [Test]
    public async Task CacheFor_is_public_for_its_max_age_with_a_tag_and_the_shortest_limit_wins()
    {
        await using var app = await StartAsync();

        using var cached = await app.GetTestClient().GetAsync("/v1/cached");
        using var limited = await app.GetTestClient().GetAsync("/v1/limited");
        using var notOk = await app.GetTestClient().GetAsync("/v1/not-ok");

        using (Assert.Multiple())
        {
            await Assert.That(cached.Headers.CacheControl?.Public).IsTrue();
            await Assert.That(cached.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.FromMinutes(5));
            await Assert.That(cached.Headers.ETag).IsNotNull();
            await Assert.That(limited.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.FromSeconds(30));
            await Assert.That(notOk.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(notOk.Headers.ETag).IsNull();
        }
    }

    [Test]
    public async Task A_cache_lifetime_cannot_be_negative()
    {
        await using var app = await StartAsync();

        await Assert.That(() => app.MapGet("/neg", () => TypedResults.Ok("x")).CacheFor(TimeSpan.FromSeconds(-1))).Throws<ArgumentOutOfRangeException>();
    }
}
