using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using Frappe.Observability;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Tests.Observability;

/// <summary>
/// The trace of a request follows its code across every await, into the use case and back, whether async methods are
/// compiled to state machines or run by the runtime (Runtime Async): spans hang from the request's, and what a callee
/// changes in its ambient context never leaks back to its caller.
/// </summary>
public sealed class TraceFlowTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddMetrics()
            .AddKeyedScoped<IUnitOfWork>("platform", (_, _) => new RecordingUnitOfWork())
            .AddHandlers(typeof(TraceFlowTests).Assembly);

        var app = builder.Build();
        app.MapGet("/traced", async (IQueryHandler<TracedQuery, TraceSnapshot> handler, CancellationToken cancellationToken) =>
        {
            var request = Activity.Current;
            await Task.Yield();
            var inside = await handler.HandleAsync(new TracedQuery(), cancellationToken);
            await Task.Delay(1, cancellationToken);
            return new TraceFlow(
                request?.TraceId.ToHexString(),
                request?.SpanId.ToHexString(),
                inside.Value,
                Activity.Current?.SpanId.ToHexString(),
                TracedQueryHandler.Marker.Value);
        });

        await app.StartAsync();
        return app;
    }

    [Test]
    public async Task A_request_keeps_its_trace_across_every_await_into_the_use_case_and_back()
    {
        ConcurrentBag<Activity> spans = [];
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Microsoft.AspNetCore" or FrappeTelemetry.Name or TracedQueryHandler.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var caller = ActivityTraceId.CreateRandom();
        await using var app = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/traced");
        request.Headers.TryAddWithoutValidation("traceparent", $"00-{caller.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        using var response = await app.GetTestClient().SendAsync(request);
        var flow = (await response.Content.ReadFromJsonAsync<TraceFlow>())!;

        var useCase = spans.Single(s => s.TraceId == caller && s.DisplayName == "query TracedQuery");
        await Assert.That(flow.RequestTraceId).IsEqualTo(caller.ToHexString());
        await Assert.That(useCase.ParentSpanId.ToHexString()).IsEqualTo(flow.RequestSpanId);
        await Assert.That(flow.Inside.TraceId).IsEqualTo(caller.ToHexString());
        await Assert.That(flow.Inside.ParentSpanId).IsEqualTo(useCase.SpanId.ToHexString());
        await Assert.That(flow.Inside.Marker).IsEqualTo("set by the use case");

        // Back in the endpoint, after more awaits: the request's span again, and nothing the use case set.
        await Assert.That(flow.SpanAfter).IsEqualTo(flow.RequestSpanId);
        await Assert.That(flow.MarkerAfter).IsNull();
    }

    private sealed record TraceFlow(string? RequestTraceId, string? RequestSpanId, TraceSnapshot Inside, string? SpanAfter, string? MarkerAfter);
}

public sealed record TracedQuery : IQuery<TraceSnapshot>;

/// <summary>What a use case saw of its trace after awaiting: the work span it started and its own ambient marker.</summary>
public sealed record TraceSnapshot(string TraceId, string ParentSpanId, string? Marker);

public sealed class TracedQueryHandler : IQueryHandler<TracedQuery, TraceSnapshot>
{
    public const string SourceName = "Frappe.Tests.TraceFlow";

    public static readonly AsyncLocal<string?> Marker = new();

    private static readonly ActivitySource Source = new(SourceName);

    public async Task<Result<TraceSnapshot>> HandleAsync(TracedQuery query, CancellationToken cancellationToken)
    {
        Marker.Value = "set by the use case";
        await Task.Yield();
        await Task.Delay(1, cancellationToken);
        using var work = Source.StartActivity("work after the awaits");
        await Task.Delay(1, cancellationToken);
        return new TraceSnapshot(work!.TraceId.ToHexString(), work.ParentSpanId.ToHexString(), Marker.Value);
    }
}
