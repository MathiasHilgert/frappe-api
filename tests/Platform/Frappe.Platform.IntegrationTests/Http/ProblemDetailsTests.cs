using System.Globalization;
using System.Text.Json.Nodes;
using Alba;
using Frappe.Platform.IntegrationTests.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.IntegrationTests.Http;

public sealed class ProblemDetailsTests
{
    private static readonly Dictionary<string, string> _expected = new(StringComparer.Ordinal)
    {
        ["validation"] =
            "{\"type\":\"urn:frappe:problem:validation\",\"title\":\"The request failed validation.\",\"status\":422,\"instance\":\"/v1/probes/problems/validation\",\"errors\":[{\"code\":\"kernel.country_code.invalid\",\"pointer\":\"/countryCode\",\"detail\":\"The country code must be two ISO 3166-1 alpha-2 letters.\",\"args\":null},{\"code\":\"probe.name.too_long\",\"pointer\":\"/name\",\"detail\":\"The name must be at most 100 characters.\",\"args\":{\"max\":100}}]}",
        ["not-found"] =
            "{\"type\":\"urn:frappe:problem:not_found\",\"title\":\"The resource was not found.\",\"status\":404,\"instance\":\"/v1/probes/problems/not-found\",\"errors\":[{\"code\":\"probe.thing.not_found\",\"pointer\":null,\"detail\":\"The thing was not found.\",\"args\":null}]}",
        ["conflict"] =
            "{\"type\":\"urn:frappe:problem:conflict\",\"title\":\"The request conflicts with the current state.\",\"status\":409,\"instance\":\"/v1/probes/problems/conflict\",\"errors\":[{\"code\":\"probe.thing.closed\",\"pointer\":null,\"detail\":\"The thing is closed.\",\"args\":null}]}",
        ["forbidden"] =
            "{\"type\":\"urn:frappe:problem:forbidden\",\"title\":\"The action is not allowed.\",\"status\":403,\"instance\":\"/v1/probes/problems/forbidden\",\"errors\":[{\"code\":\"probe.thing.forbidden\",\"pointer\":null,\"detail\":\"The thing is not yours.\",\"args\":null}]}",
        ["malformed"] =
            "{\"type\":\"urn:frappe:problem:malformed_request\",\"title\":\"The request is malformed.\",\"status\":400,\"instance\":\"/v1/probes/problems/malformed\",\"errors\":[{\"code\":\"platform.paging.invalid_cursor\",\"pointer\":null,\"detail\":\"The cursor is invalid; start again from the first page.\",\"args\":null}]}",
        ["unexpected"] =
            "{\"type\":\"urn:frappe:problem:internal\",\"title\":\"The server failed to complete the request.\",\"status\":500,\"instance\":\"/v1/probes/problems/unexpected\",\"errors\":[{\"code\":\"probe.thing.unexpected\",\"pointer\":null,\"detail\":\"The thing broke.\",\"args\":null}]}",
    };

    [Theory]
    [InlineData("validation", 422)]
    [InlineData("not-found", 404)]
    [InlineData("conflict", 409)]
    [InlineData("forbidden", 403)]
    [InlineData("malformed", 400)]
    [InlineData("unexpected", 500)]
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

        ShouldBeProblem(result, await result.ReadAsTextAsync(), _expected[kind]);
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
        body.ShouldContain("El código de país debe ser de dos letras ISO 3166-1 alfa-2.");
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

        ShouldBeProblem(
            result,
            await result.ReadAsTextAsync(),
            "{\"type\":\"urn:frappe:problem:malformed_request\",\"title\":\"The request is malformed.\",\"status\":400,\"instance\":\"/v1/probes/echoes\",\"errors\":[{\"code\":\"platform.request.malformed\",\"pointer\":\"/name\",\"detail\":\"The request is malformed.\",\"args\":null}]}"
        );
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

        string body = await result.ReadAsTextAsync();
        body.ShouldNotContain("s3cret");
        logs.Records.Where(static record => record.Level >= LogLevel.Error)
            .Select(static record => record.EventId.Id)
            .ShouldBe([201]);
        ShouldBeProblem(
            result,
            body,
            "{\"type\":\"urn:frappe:problem:internal\",\"title\":\"The server failed to complete the request.\",\"status\":500,\"instance\":\"/v1/probes/failures\",\"errors\":[{\"code\":\"platform.request.failed\",\"pointer\":null,\"detail\":\"The server failed to complete the request.\",\"args\":null}]}"
        );
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

        ShouldBeProblem(
            result,
            await result.ReadAsTextAsync(),
            "{\"type\":\"urn:frappe:problem:timeout\",\"title\":\"The request timed out.\",\"status\":504,\"instance\":\"/v1/probes/waits\",\"errors\":[{\"code\":\"platform.request.timeout\",\"pointer\":null,\"detail\":\"The request timed out.\",\"args\":null}]}"
        );
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
        ShouldBeProblem(
            result,
            await result.ReadAsTextAsync(),
            "{\"type\":\"urn:frappe:problem:rate_limited\",\"title\":\"Too many requests.\",\"status\":429,\"instance\":\"/v1/probes/problems/conflict\",\"errors\":[{\"code\":\"platform.rate_limit.exceeded\",\"pointer\":null,\"detail\":\"Too many requests; retry later.\",\"args\":null}]}"
        );
    }

    // The whole body is the contract: every member but the per-request trace id is compared.
    private static void ShouldBeProblem(IScenarioResult result, string body, string expected)
    {
        HttpResponse response = result.Context.Response;
        response.ContentType.ShouldBe("application/problem+json");
        // The exception handler middleware adds no-cache; no-store is what keeps every cache from storing it.
        response
            .Headers.CacheControl.ToString()
            .Split(',')
            .ShouldContain("no-store", StringComparer.Ordinal);
        JsonObject problem = JsonNode.Parse(body).ShouldBeOfType<JsonObject>();
        problem["traceId"].ShouldNotBeNull().GetValue<string>().ShouldMatch("^[0-9a-f]{32}$");
        problem.Remove("traceId");
        problem.ToJsonString().ShouldBe(expected);
    }
}
