using System.Text.Json;
using Alba;
using Frappe.Lexicon.Infrastructure;
using Frappe.Platform.Http;
using Frappe.Platform.IntegrationTests.Messaging;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.IntegrationTests.Http;

public sealed class HttpSurfaceTests
{
    [Fact]
    public async Task A_response_writes_every_member_in_its_house_wire_form()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Post.Json(new { name = "Pilar" }).ToUrl("/v1/probes/echoes");
            scenario.StatusCodeShouldBeOk();
        });

        (await result.ReadAsTextAsync()).ShouldBe(
            "{\"name\":\"Pilar\",\"date\":\"2026-04-02\",\"color\":\"dark_red\",\"note\":null}"
        );
    }

    // Nobody reads the answer to an aborted request, so it must not page anyone as a server error.
    [Fact]
    public async Task A_client_disconnect_writes_no_body_logs_at_debug_and_is_no_server_error()
    {
        LogCapture logs = new();
        StatusRecorder recorder = new();
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            builder =>
            {
                // A provider rule outranks the Default and Microsoft levels the host configuration sets.
                builder.Logging.AddProvider(logs).AddFilter<LogCapture>(null, LogLevel.Debug);
                builder.Services.AddSingleton<IStartupFilter>(recorder);
            }
        );
        using HttpClient client = host.Server.CreateClient();
        using CancellationTokenSource disconnect = new(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            client.GetAsync(new Uri("/v1/probes/waits", UriKind.Relative), disconnect.Token)
        );

        (int status, long bodyLength) = await recorder.Finished.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        status.ShouldBe(StatusCodes.Status499ClientClosedRequest);
        bodyLength.ShouldBe(0);
        // ASP.NET Core's exception handler middleware logs the abort itself, before any IExceptionHandler runs.
        logs.Records.ShouldContain(static record =>
            record.Level == LogLevel.Debug
            && string.Equals(record.EventId.Name, "RequestAborted", StringComparison.Ordinal)
        );
        logs.Records.ShouldNotContain(record => record.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task A_request_times_out_after_30_seconds_by_default()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        RequestTimeoutPolicy? policy = host
            .Services.GetRequiredService<IOptions<RequestTimeoutOptions>>()
            .Value.DefaultPolicy;

        policy.ShouldNotBeNull().Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    // A module keeps its error catalog in its Application assembly, so its texts must load from there too.
    [Fact]
    public async Task Error_texts_load_from_every_assembly_of_a_module()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new LexiconModule());

        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/v1/lexicons/words/zzz");
            scenario.StatusCodeShouldBe(404);
        });

        (await result.ReadAsTextAsync()).ShouldContain(
            "\"code\":\"lexicon.word.unknown\",\"pointer\":null,\"detail\":\"The lexicon has no such word.\""
        );
    }

    [Fact]
    public async Task A_rate_limit_out_of_range_fails_the_start()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
        builder.Configuration["RateLimiting:PublicPermitLimit"] = "0";
        builder.AddHttp();
        using IHost host = builder.Build();

        OptionsValidationException error = await Should.ThrowAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken)
        );

        error.Message.ShouldContain("PublicPermitLimit");
    }

    // The document is the contract clients generate from, so every change to it shows in review.
    [Fact]
    public async Task The_openapi_document_matches_its_snapshot()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        Snapshot.ShouldMatch((await OpenApiAsync(host)).GetRawText(), "openapi-probe");
    }

    [Fact]
    public async Task The_openapi_document_describes_converted_types_as_their_wire_primitives()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        JsonElement schemas = (await OpenApiAsync(host))
            .GetProperty("components")
            .GetProperty("schemas");

        string.Join(
                " | ",
                Compact(schemas.GetProperty("LocalDate")),
                Compact(schemas.GetProperty("EchoColor"))
            )
            .ShouldBe(
                "{\"type\":\"string\",\"format\":\"date\"} | {\"enum\":[\"dark_red\"],\"type\":\"string\"}"
            );
    }

    [Fact]
    public async Task The_openapi_document_summarizes_every_operation_and_describes_the_problem_shape()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IReadOnlyList<string> violations = OpenApiStandard.Violations(await OpenApiAsync(host));

        // The probe's own records carry no XML docs into the document: only assemblies Platform.Http references do.
        violations.ShouldAllBe(static violation =>
            violation.StartsWith("Echo", StringComparison.Ordinal)
            && violation.EndsWith(": no description", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task The_openapi_description_test_catches_an_operation_without_a_summary()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new NonconformingModule());

        IReadOnlyList<string> violations = OpenApiStandard.Violations(await OpenApiAsync(host));

        violations.ShouldContain(
            "POST /v1/rogues/create-rogue: no summary",
            StringComparer.Ordinal
        );
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    public async Task The_openapi_document_and_scalar_are_not_published_outside_development(
        string url
    )
    {
        await using IAlbaHost host = await HttpHost.StartAsync(
            new ProbeModule(),
            environment: Environments.Production
        );

        await host.Scenario(scenario =>
        {
            scenario.Get.Url(url);
            scenario.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task Scalar_is_published_in_development()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/scalar/");
            scenario.StatusCodeShouldBeOk();
        });
    }

    [Fact]
    public async Task Conforming_endpoints_pass_the_route_standard()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new ProbeModule());

        IReadOnlyList<string> violations = RouteStandard.Violations(
            host.Services.GetRequiredService<EndpointDataSource>(),
            new HashSet<string>(StringComparer.Ordinal)
        );

        violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_route_standard_reports_each_broken_rule()
    {
        await using IAlbaHost host = await HttpHost.StartAsync(new NonconformingModule());

        IReadOnlyList<string> violations = RouteStandard.Violations(
            host.Services.GetRequiredService<EndpointDataSource>(),
            new HashSet<string>(StringComparer.Ordinal)
        );

        violations.ShouldBe(
            [
                "/v1/rogues/create-rogue: declares no single rate-limit policy",
                "/v1/rogues/create-rogue: allows anonymous access but is not on the declared list",
                "/v1/rogues/traces: maps TRACE",
            ],
            ignoreOrder: true
        );
    }

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    private static async Task<JsonElement> OpenApiAsync(IAlbaHost host)
    {
        IScenarioResult result = await host.Scenario(static scenario =>
        {
            scenario.Get.Url("/openapi/v1.json");
            scenario.StatusCodeShouldBeOk();
        });
        return JsonDocument.Parse(await result.ReadAsTextAsync()).RootElement.Clone();
    }

    // Records the final status and body size of the request, once the pipeline has finished with it.
    private sealed class StatusRecorder : IStartupFilter
    {
        private readonly TaskCompletionSource<(int, long)> _finished = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<(int Status, long BodyLength)> Finished => _finished.Task;

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    async (context, nextMiddleware) =>
                    {
                        CountingStream body = new(context.Response.Body);
                        context.Response.Body = body;
                        try
                        {
                            await nextMiddleware(context);
                        }
                        finally
                        {
                            _finished.TrySetResult((context.Response.StatusCode, body.Written));
                        }
                    }
                );
                next(app);
            };
    }
}
