using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Frappe.Http;
using Frappe.Platform.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>
/// The console's origins (<c>Http:Console:Origins</c>): what an entry may be, the credentialed CORS policy they get, and
/// the <c>Origin</c> check of unsafe requests that carry a browser credential.
/// </summary>
public sealed class ConsoleOriginTests
{
    private const string Key = "Http:Console:Origins";

    /// <summary>A host with one route under the console policy; a request "carries a browser credential" with header <c>X-Cookie</c>.</summary>
    private static async Task<WebApplication> StartAsync(string? origins)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [Key] = origins });
        builder.Services.AddFrappeHttp().AddErrorMessages(PlatformTestMessages.Assembly);
        var app = builder.Build();
        app.UseFrappeHttp();
        app.UseConsoleOriginCheck(httpContext => httpContext.Request.Headers.ContainsKey("X-Cookie"));
        var v1 = app.MapGroup("/v1").RequireCors(ConsoleOriginRegistration.ConsolePolicy);
        v1.MapMethods("/thing", ["GET", "POST", "PUT", "PATCH", "DELETE"], () => "done");
        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> SendAsync(WebApplication app, string method, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/v1/thing");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await app.GetTestClient().SendAsync(request);
    }

    [Test]
    [Arguments("https://app.frappe.la", "https://app.frappe.la")]
    [Arguments("HTTPS://App.Frappe.LA:443", "https://app.frappe.la")]
    [Arguments("https://app.frappe.la:8443", "https://app.frappe.la:8443")]
    [Arguments("http://localhost:5173", "http://localhost:5173")]
    [Arguments("http://127.0.0.1:5173", "http://127.0.0.1:5173")]
    public async Task An_https_origin_or_http_on_localhost_is_a_console_origin_as_a_browser_writes_it(string entry, string origin)
    {
        // Act
        var options = new ConsoleOriginOptions { Origins = $" {entry} , https://admin.frappe.la" };

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.Allowed()).IsEquivalentTo([origin, "https://admin.frappe.la"]);
            await Assert.That(options.Allows(origin)).IsTrue();
        }
    }

    [Test]
    [Arguments("http://app.frappe.la")]
    [Arguments("https://app.frappe.la/")]
    [Arguments("https://app.frappe.la/console")]
    [Arguments("https://app.frappe.la?x=1")]
    [Arguments("https://app.frappe.la#top")]
    [Arguments("https://user@app.frappe.la")]
    [Arguments("app.frappe.la")]
    [Arguments("*")]
    [Arguments("ftp://app.frappe.la")]
    public async Task Anything_else_fails_at_startup_naming_the_key_and_the_entry(string entry)
    {
        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(async () => await StartAsync($"https://app.frappe.la,{entry}"));

        // Assert
        await Assert.That(failure!.Message).Contains($"Configuration '{Key}' has an entry that is not an https origin (or http on localhost): '{entry}'.");
    }

    [Test]
    public async Task The_list_is_parsed_once_and_a_request_origin_is_looked_up_as_a_browser_sends_it()
    {
        // Arrange
        var options = new ConsoleOriginOptions { Origins = "https://App.Frappe.la" };

        // Act
        var first = options.Allowed();
        var second = options.Allowed();
        var exact = options.Allows("https://app.frappe.la");
        var withSlash = options.Allows("https://app.frappe.la/");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(second).IsSameReferenceAs(first);
            await Assert.That(exact).IsTrue();
            await Assert.That(withSlash).IsFalse();
        }
    }

    [Test]
    public async Task A_changed_list_is_parsed_again()
    {
        // Arrange
        var options = new ConsoleOriginOptions { Origins = "https://App.Frappe.la" };
        _ = options.Allowed();

        // Act
        options.Origins = "https://admin.frappe.la";
        var allowed = options.Allowed();
        var previous = options.Allows("https://app.frappe.la");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(allowed).IsEquivalentTo(["https://admin.frappe.la"]);
            await Assert.That(previous).IsFalse();
        }
    }

    [Test]
    public async Task With_no_origin_configured_nothing_is_allowed_and_null_is_no_origin()
    {
        // Act
        var none = new ConsoleOriginOptions();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(none.Allowed()).IsEmpty();
            await Assert.That(none.Allows("https://app.frappe.la")).IsFalse();
            await Assert.That(new ConsoleOriginOptions { Origins = "https://app.frappe.la" }.Allows(null)).IsFalse();
            await Assert.That(new ConsoleOriginOptions { Origins = "https://app.frappe.la" }.Allows("null")).IsFalse();
        }
    }

    [Test]
    public async Task A_console_origin_gets_credentialed_cors_answers_and_any_other_none()
    {
        // Arrange
        await using var app = await StartAsync("https://app.frappe.la");

        using var preflight = await SendAsync(app, "OPTIONS", ("Origin", "https://app.frappe.la"), ("Access-Control-Request-Method", "PATCH"));
        using var foreign = await SendAsync(app, "OPTIONS", ("Origin", "https://evil.example"), ("Access-Control-Request-Method", "PATCH"));

        // Act
        using var actual = await SendAsync(app, "GET", ("Origin", "https://app.frappe.la"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(preflight.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(preflight.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://app.frappe.la");
            await Assert.That(preflight.Headers.GetValues("Access-Control-Allow-Credentials").Single()).IsEqualTo("true");
            await Assert.That(preflight.Headers.GetValues("Access-Control-Allow-Methods").Single()).Contains("PATCH");
            await Assert.That(preflight.Headers.GetValues("Access-Control-Max-Age").Single()).IsEqualTo("600");
            await Assert.That(foreign.Headers.Any(h => h.Key.StartsWith("Access-Control-", StringComparison.Ordinal))).IsFalse();
            await Assert.That(actual.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://app.frappe.la");
            await Assert.That(actual.Headers.GetValues("Access-Control-Expose-Headers").Single()).Contains("Retry-After");
            await Assert.That(actual.Headers.Vary).Contains("Origin");
            await Assert.That(actual.Headers.Vary.Count(value => value == "Origin")).IsEqualTo(1);
            await Assert.That(foreign.Headers.Vary).Contains("Origin");
        }
    }

    [Test]
    [Arguments("POST")]
    [Arguments("PUT")]
    [Arguments("PATCH")]
    [Arguments("DELETE")]
    public async Task An_unsafe_request_with_a_browser_credential_needs_a_console_origin(string method)
    {
        // Arrange
        await using var app = await StartAsync("https://app.frappe.la");

        using var missing = await SendAsync(app, method, ("X-Cookie", "1"), ("Accept-Language", "es"));
        using var foreign = await SendAsync(app, method, ("X-Cookie", "1"), ("Origin", "https://evil.example"));
        using var console = await SendAsync(app, method, ("X-Cookie", "1"), ("Origin", "https://app.frappe.la"));
        using var anonymous = await SendAsync(app, method);

        // Act
        using var problem = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(missing.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo("urn:frappe:problem:forbidden");
            await Assert.That(problem.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()).IsEqualTo(ConsoleOriginCheck.OriginForbiddenCode);
            await Assert.That(problem.RootElement.GetProperty("errors")[0].GetProperty("message").GetString()).IsNotNull();
            await Assert.That(missing.Content.Headers.ContentLanguage.Single()).IsEqualTo("es");
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(console.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    public async Task Every_refusal_is_counted_by_why_without_the_origin_and_nothing_else_is()
    {
        // Arrange
        await using var app = await StartAsync("https://app.frappe.la");
        using var capture = new OriginCapture(app.Services.GetRequiredService<IMeterFactory>());

        using var missing = await SendAsync(app, "POST", ("X-Cookie", "1"));
        using var foreign = await SendAsync(app, "DELETE", ("X-Cookie", "1"), ("Origin", "https://evil.example"));
        using var opaque = await SendAsync(app, "PUT", ("X-Cookie", "1"), ("Origin", "null"));
        using var console = await SendAsync(app, "POST", ("X-Cookie", "1"), ("Origin", "https://app.frappe.la"));
        using var anonymous = await SendAsync(app, "POST", ("Origin", "https://evil.example"));

        // Act
        using var safe = await SendAsync(app, "GET", ("X-Cookie", "1"), ("Origin", "https://evil.example"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That(capture.Measurements).IsEquivalentTo(
            [
                ("frappe.http.origin.refused", 1L, "frappe.http.origin.reason=missing"),
                ("frappe.http.origin.refused", 1L, "frappe.http.origin.reason=not_allowed"),
                ("frappe.http.origin.refused", 1L, "frappe.http.origin.reason=not_allowed"),
            ]);
        }
    }

    [Test]
    public async Task Without_its_metrics_registered_a_refusal_is_still_a_403_and_simply_not_counted()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [Key] = "https://app.frappe.la" });
        builder.Services.AddFrappeHttp().AddErrorMessages(PlatformTestMessages.Assembly);
        builder.Services.RemoveAll<ConsoleOriginMetrics>();
        await using var app = builder.Build();
        app.UseFrappeHttp();
        app.UseConsoleOriginCheck(httpContext => httpContext.Request.Headers.ContainsKey("X-Cookie"));
        app.MapPost("/v1/thing", () => "done");
        await app.StartAsync();

        // Act
        using var refused = await SendAsync(app, "POST", ("X-Cookie", "1"), ("Origin", "https://evil.example"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(app.Services.GetService<ConsoleOriginMetrics>()).IsNull();
            await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    [Arguments(OriginRefusal.Missing, "missing")]
    [Arguments(OriginRefusal.NotAllowed, "not_allowed")]
    public async Task Each_refusal_has_its_own_tag_and_one_without_a_tag_is_a_bug(OriginRefusal refusal, string tag)
    {
        // Arrange
        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var metrics = new ConsoleOriginMetrics(meters);
        using var capture = new OriginCapture(meters);

        // Act
        metrics.Refused(refusal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(capture.Measurements.Single()).IsEqualTo(("frappe.http.origin.refused", 1L, $"frappe.http.origin.reason={tag}"));
            await Assert.That(Enum.GetValues<OriginRefusal>()).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_refusal_without_a_tag_is_a_bug_and_counts_nothing()
    {
        // Arrange
        var meters = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
        var metrics = new ConsoleOriginMetrics(meters);
        using var capture = new OriginCapture(meters);

        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => metrics.Refused((OriginRefusal)99));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(capture.Measurements).IsEmpty();
        }
    }

    [Test]
    public async Task A_safe_request_is_never_checked()
    {
        // Arrange
        await using var app = await StartAsync("https://app.frappe.la");

        using var get = await SendAsync(app, "GET", ("X-Cookie", "1"), ("Origin", "https://evil.example"));

        // Act
        using var bare = await SendAsync(app, "GET");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(bare.Headers.Vary).Contains("Origin");
        }
    }

    [Test]
    public async Task With_several_console_origins_an_answer_still_varies_by_origin_once()
    {
        // Arrange
        await using var app = await StartAsync("https://app.frappe.la, https://admin.frappe.la");

        // Act
        using var actual = await SendAsync(app, "GET", ("Origin", "https://admin.frappe.la"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(actual.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://admin.frappe.la");
            await Assert.That(actual.Headers.Vary.Count(value => value == "Origin")).IsEqualTo(1);
        }
    }

    [Test]
    public async Task A_problem_needs_at_least_one_error()
    {
        // Act
        var none = Assert.Throws<ArgumentOutOfRangeException>(() => ErrorsProblemHttpResult.Of());
        var missing = Assert.Throws<ArgumentNullException>(() => ErrorsProblemHttpResult.Of(null!));
        var one = ErrorsProblemHttpResult.Of(new Frappe.Results.ForbiddenError("x.y"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(none).IsNotNull();
            await Assert.That(missing).IsNotNull();
            await Assert.That(one is IResult).IsTrue();
        }
    }

    /// <summary>Every measurement of one meter factory's instruments, as (name, value, "key=value" of each tag).</summary>
    private sealed class OriginCapture : IDisposable
    {
        private readonly MeterListener _listener = new();

        public OriginCapture(IMeterFactory meters)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter.Scope, meters) && instrument.Name == ConsoleOriginMetrics.RefusedMetric)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                Measurements.Enqueue((instrument.Name, value, string.Join(',', tags.ToArray().Select(t => $"{t.Key}={t.Value}")))));
            _listener.Start();
        }

        public ConcurrentQueue<(string Name, long Value, string Tags)> Measurements { get; } = new();

        public void Dispose() => _listener.Dispose();
    }

    [Test]
    public async Task A_console_request_refused_by_the_rate_limit_still_carries_its_CORS_headers()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Key] = "https://app.frappe.la",
            ["Http:RateLimit:PermitLimit"] = "1",
            ["Http:RateLimit:Window"] = "00:10:00",
        });
        builder.Services.AddFrappeHttp().AddErrorMessages(PlatformTestMessages.Assembly);
        await using var app = builder.Build();
        app.UseFrappeHttp();
        app.MapGroup("/v1")
            .RequireCors(ConsoleOriginRegistration.ConsolePolicy)
            .RequireRateLimiting(HttpRegistration.AnonymousPolicy)
            .MapGet("/thing", () => "done");
        await app.StartAsync();
        using var first = await SendAsync(app, "GET", ("Origin", "https://app.frappe.la"));

        // Act
        using var limited = await SendAsync(app, "GET", ("Origin", "https://app.frappe.la"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(limited.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://app.frappe.la");
        }
    }
}
