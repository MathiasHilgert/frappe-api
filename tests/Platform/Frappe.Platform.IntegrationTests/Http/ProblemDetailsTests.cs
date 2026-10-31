using System.Globalization;
using System.Text;
using Alba;
using Frappe.Platform.IntegrationTests.Messaging;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.IntegrationTests.Http;

// Every problem body is a contract, compared whole with its snapshot next to this file.
public sealed class ProblemDetailsTests
{
    [Theory]
    [InlineData("validation", 422)]
    [InlineData("not-found", 404)]
    [InlineData("conflict", 409)]
    [InlineData("forbidden", 403)]
    [InlineData("unauthorized", 401)]
    [InlineData("malformed", 400)]
    public async Task An_error_result_is_answered_with_the_house_problem_of_its_type(
        string kind,
        int status
    )
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(scenario =>
        {
            scenario.Get.Url($"/v1/probes/problems/{kind}");
            scenario.StatusCodeShouldBe(status);
        });

        await ShouldBeProblemAsync(result, $"problem-{kind}");
    }

    [Fact]
    public async Task An_unauthorized_problem_asks_for_authentication()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/unauthorized");
            scenario.StatusCodeShouldBe(401);
        });

        result.Context.Response.Headers.WWWAuthenticate.ToString().ShouldBe("Bearer");
        (await result.ReadAsTextAsync()).ShouldNotContain("SECRET");
    }

    // A failure the caller cannot fix speaks only of the platform: the producer's code and text stay in the log.
    [Fact]
    public async Task A_returned_server_error_hides_its_errors_and_logs_them_once()
    {
        LogCapture logs = new();
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            builder => builder.Logging.AddProvider(logs)
        );

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/unexpected");
            scenario.StatusCodeShouldBe(500);
        });

        (await result.ReadAsTextAsync()).ShouldNotContain("SECRET");
        LogCapture.Record logged = logs
            .Records.Where(static record => record.Level >= LogLevel.Error)
            .ShouldHaveSingleItem();
        logged.EventId.Id.ShouldBe(202);
        logged.State["ErrorCodes"].ShouldBe("probe.thing.unexpected");
        await ShouldBeProblemAsync(result, "problem-returned-500");
    }

    // The worst class wins: one server error among client errors still hides every producer error.
    [Fact]
    public async Task A_server_error_among_client_errors_is_a_500_that_hides_them_all()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/mixed");
            scenario.StatusCodeShouldBe(500);
        });

        string body = await result.ReadAsTextAsync();
        body.ShouldNotContain("SECRET");
        body.ShouldNotContain("probe.");
        await ShouldBeProblemAsync(result, "problem-mixed-500");
    }

    // A code shipped without a text must not leak its English description, which may hold internals.
    [Fact]
    public async Task A_code_without_a_text_gets_the_generic_text_of_its_type()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/untranslated?locale=es");
            scenario.StatusCodeShouldBe(422);
        });

        await ShouldBeProblemAsync(result, "problem-untranslated");
    }

    // The body locale comes from ?locale= first, so an error speaks the language the client asked the body in.
    [Fact]
    public async Task The_locale_query_wins_over_accept_language_for_error_texts()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/validation?locale=es");
            scenario.WithRequestHeader("Accept-Language", "en");
            scenario.StatusCodeShouldBe(422);
        });

        string body = await result.ReadAsTextAsync();
        body.ShouldContain("El identificador debe ser un UUIDv7 RFC 9562 no vacío.");
    }

    [Fact]
    public async Task A_wrong_json_type_is_a_malformed_request_pointing_at_the_member()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Post.Text("{\"name\":5}").ToUrl("/v1/probes/echoes");
            scenario.WithRequestHeader("Content-Type", "application/json");
            scenario.StatusCodeShouldBe(400);
        });

        await ShouldBeProblemAsync(result, "problem-malformed-json");
    }

    [Fact]
    public async Task An_unknown_member_is_a_malformed_request()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Post.Text("{\"nmae\":\"x\"}").ToUrl("/v1/probes/echoes");
            scenario.WithRequestHeader("Content-Type", "application/json");
            scenario.StatusCodeShouldBe(400);
        });

        string body = await result.ReadAsTextAsync();
        body.ShouldContain("\"code\":\"platform.request.malformed\"");
    }

    [Fact]
    public async Task An_unhandled_exception_is_a_500_without_internals_logged_once_as_an_error()
    {
        LogCapture logs = new();
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            builder => builder.Logging.AddProvider(logs)
        );

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/failures");
            scenario.StatusCodeShouldBe(500);
        });

        (await result.ReadAsTextAsync()).ShouldNotContain("s3cret");
        logs.Records.Where(static record => record.Level >= LogLevel.Error)
            .Select(static record => record.EventId.Id)
            .ShouldBe([201]);
        await ShouldBeProblemAsync(result, "problem-unhandled-500");
    }

    [Fact]
    public async Task A_request_that_outlives_its_timeout_is_a_504_problem()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            static builder =>
                builder.Services.PostConfigure<RequestTimeoutOptions>(static options =>
                    options.DefaultPolicy = new RequestTimeoutPolicy
                    {
                        Timeout = TimeSpan.FromMilliseconds(100),
                        TimeoutStatusCode = options.DefaultPolicy?.TimeoutStatusCode,
                        WriteTimeoutResponse = options.DefaultPolicy?.WriteTimeoutResponse,
                    }
                )
        );

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/waits");
            scenario.StatusCodeShouldBe(504);
        });

        await ShouldBeProblemAsync(result, "problem-timeout");
    }

    [Fact]
    public async Task A_rejected_request_is_a_429_problem_with_retry_after()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            static builder => builder.Configuration["RateLimiting:PublicPermitLimit"] = "1"
        );
        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/conflict");
            scenario.StatusCodeShouldBe(409);
        });

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/probes/problems/conflict");
            scenario.StatusCodeShouldBe(429);
        });

        int.Parse(
                result.Context.Response.Headers.RetryAfter.ToString(),
                CultureInfo.InvariantCulture
            )
            .ShouldBeInRange(1, 60);
        await ShouldBeProblemAsync(result, "problem-rate-limited");
    }

    [Theory]
    [InlineData("GET", "/v1/nowhere", null, 404, "problem-route-not-found")]
    [InlineData("GET", "/v1/probes/things/not-a-guid", null, 404, "problem-route-constraint")]
    [InlineData("DELETE", "/v1/probes/echoes", null, 405, "problem-method-not-allowed")]
    [InlineData("POST", "/v1/probes/echoes", "text/plain", 415, "problem-unsupported-media-type")]
    public async Task A_request_no_endpoint_answers_gets_the_house_problem(
        string method,
        string url,
        string? contentType,
        int status,
        string snapshot
    )
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(scenario =>
        {
            scenario.ConfigureHttpContext(context =>
            {
                context.Request.Method = method;
                context.Request.Path = url;
                if (contentType is not null)
                {
                    context.Request.ContentType = contentType;
                    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
                }
            });
            scenario.StatusCodeShouldBe(status);
        });

        await ShouldBeProblemAsync(result, snapshot);
    }

    // Kestrel enforces the endpoint's size limit while the body is read, so this runs on a real server.
    [Fact]
    public async Task A_body_over_the_size_limit_is_a_413_problem()
    {
        await using WebApplication app = await HttpHost.StartKestrelAsync(new ProbeModule());
        using HttpClient client = new() { BaseAddress = new Uri(app.Urls.First()) };

        using HttpResponseMessage response = await client.PostAsync(
            new Uri("/v1/probes/uploads", UriKind.Relative),
            new StringContent(
                $"{{\"name\":\"{new string('x', 100)}\"}}",
                Encoding.UTF8,
                "application/json"
            ),
            TestContext.Current.CancellationToken
        );

        ((int)response.StatusCode).ShouldBe(413);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        Snapshot.ShouldMatch(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            "problem-too-large"
        );
    }

    private static async Task ShouldBeProblemAsync(IScenarioResult result, string snapshot)
    {
        HttpResponse response = result.Context.Response;
        response.ContentType.ShouldBe("application/problem+json");
        // The exception handler middleware adds no-cache; no-store is what keeps every cache from storing it.
        response
            .Headers.CacheControl.ToString()
            .Split(',')
            .ShouldContain("no-store", StringComparer.Ordinal);
        string body = await result.ReadAsTextAsync();
        body.ShouldMatch("\"traceId\":\"[0-9a-f]{32}\"");
        Snapshot.ShouldMatch(body, snapshot);
    }
}
