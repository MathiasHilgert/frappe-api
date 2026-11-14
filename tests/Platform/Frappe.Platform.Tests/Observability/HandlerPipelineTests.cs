using System.Diagnostics;
using Frappe.Observability;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.Tests.Observability;

[NotInParallel(nameof(HandlerPipelineTests))]
public sealed class HandlerPipelineTests : IDisposable
{
    private readonly TelemetryCapture _capture = new();
    private readonly ServiceProvider _services;

    public HandlerPipelineTests()
    {
        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new CaptureLoggerProvider(_capture)))
            .AddMetrics()
            // Commands run in their module's unit of work; this suite looks at telemetry, so any one will do.
            .AddKeyedScoped<IUnitOfWork>("platform", (_, _) => new RecordingUnitOfWork())
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Test]
    public async Task Resolves_handlers_from_the_scanned_assembly()
    {
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>();

        var result = await handler.HandleAsync(new Ping("a"), CancellationToken.None);

        await Assert.That(result.Value).IsEqualTo("pong:a");
    }

    [Test]
    public async Task Successful_query_emits_a_span_and_a_duration_measurement()
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>()
            .HandleAsync(new Ping("b"), CancellationToken.None);

        var span = _capture.SingleSpan("query Ping");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Kind)).IsEqualTo("query");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Module)).IsEqualTo("platform");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.UseCase)).IsEqualTo("Ping");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("success");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);

        var measurement = _capture.SingleMeasurement("Ping");
        await Assert.That(measurement.Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("success");
        await Assert.That(measurement.Value).IsGreaterThanOrEqualTo(0d);
    }

    [Test]
    public async Task Business_failure_is_recorded_as_failure_without_marking_the_span_as_error()
    {
        using var scope = _services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RejectedCommand, Unit>>()
            .HandleAsync(new RejectedCommand("c"), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        var span = _capture.SingleSpan("command RejectedCommand");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("failure");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.ErrorCode)).IsEqualTo("test.rejected");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(_capture.SingleMeasurement("RejectedCommand").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("failure");
        await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Information && l.Message.Contains("test.rejected", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Exception_is_recorded_on_the_span_and_rethrown()
    {
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExplodingCommand, Unit>>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(new ExplodingCommand("d"), CancellationToken.None));

        var span = _capture.SingleSpan("command ExplodingCommand");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("exception");
        await Assert.That(span.GetTagItem("error.type")).IsEqualTo(typeof(InvalidOperationException).FullName);
        await Assert.That(_capture.SingleMeasurement("ExplodingCommand").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("exception");
        await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Error)).IsTrue();
    }

    [Test]
    public async Task A_rejection_at_commit_is_rethrown_and_logged_as_a_rejection_not_an_incident()
    {
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RejectedAtCommitCommand, Unit>>();

        await Assert.ThrowsAsync<LostRaceException>(() => handler.HandleAsync(new RejectedAtCommitCommand("r"), CancellationToken.None));

        var span = _capture.SingleSpan("command RejectedAtCommitCommand");
        await Assert.That(span.Status).IsNotEqualTo(ActivityStatusCode.Error);
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("failure");
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.ErrorCode)).IsEqualTo("test.changed_concurrently");
        await Assert.That(_capture.Logs.Any(l => l.Level >= LogLevel.Warning)).IsFalse();
        await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Information && l.Message.Contains("test.changed_concurrently", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Caller_cancellation_is_not_an_incident()
    {
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SlowQuery, string>>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => handler.HandleAsync(new SlowQuery("e"), cancellation.Token));

        var span = _capture.SingleSpan("query SlowQuery");
        await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
        await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("cancelled");
        await Assert.That(_capture.SingleMeasurement("SlowQuery").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("cancelled");
        await Assert.That(_capture.Logs.Any(l => l.Level >= LogLevel.Warning)).IsFalse();
    }

    [Test]
    public async Task Registering_the_same_assembly_twice_keeps_one_handler_per_port()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IQueryHandler<Ping, string>>();

        await Assert.That(handlers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task A_second_handler_for_the_same_port_is_rejected_at_registration()
    {
        var services = new ServiceCollection()
            .AddScoped<IQueryHandler<Ping, string>, ConflictingPingHandler<int>>();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddHandlers(typeof(HandlerPipelineTests).Assembly));

        await Assert.That(exception.Message).Contains("Ping");
    }

    [Test]
    public async Task Keyed_registrations_of_a_port_are_left_alone()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddKeyedScoped<IQueryHandler<Ping, string>, ConflictingPingHandler<int>>("legacy")
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>()
            .HandleAsync(new Ping("k"), CancellationToken.None);

        await Assert.That(result.Value).IsEqualTo("pong:k");
    }

    public void Dispose()
    {
        _services.Dispose();
        _capture.Dispose();
    }
}
