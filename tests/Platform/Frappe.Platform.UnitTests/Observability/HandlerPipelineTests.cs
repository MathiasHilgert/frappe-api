using System.Diagnostics;
using Frappe.Observability;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>The instrumented handler pipeline: spans, measurements, logs and results for each way a handler can end.</summary>
[NotInParallel(nameof(HandlerPipelineTests))]
public sealed class HandlerPipelineTests : IDisposable
{
    private readonly TelemetryCapture _capture = new();
    private readonly RecordingUnitOfWork _unitOfWork = new();
    private readonly ServiceProvider _services;
    private readonly RecordingUseCaseGate _gate;

    public HandlerPipelineTests()
    {
        _gate = new RecordingUseCaseGate(_unitOfWork);
        _services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(new CaptureLoggerProvider(_capture)))
            .AddMetrics()
            // Commands run in their module's unit of work; this suite looks at telemetry, so any one will do.
            .AddKeyedScoped<IUnitOfWork>("platform", (_, _) => _unitOfWork)
            .AddSingleton<IUseCaseGate>(_gate)
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Test]
    public async Task Resolves_handlers_from_the_scanned_assembly()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>();

        // Act
        var result = await handler.HandleAsync(new Ping("a"), CancellationToken.None);

        // Assert
        await Assert.That(result.Value).IsEqualTo("pong:a");
    }

    [Test]
    public async Task Successful_query_emits_a_span_and_a_duration_measurement()
    {
        // Arrange
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>()
            .HandleAsync(new Ping("b"), CancellationToken.None);

        // Act
        var span = _capture.SingleSpan("query Ping");

        // Assert
        var measurement = _capture.SingleMeasurement("Ping");
        using (Assert.Multiple())
        {
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Kind)).IsEqualTo("query");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Module)).IsEqualTo("platform");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.UseCase)).IsEqualTo("Ping");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("success");
            await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
            await Assert.That(measurement.Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("success");
            await Assert.That(measurement.Value).IsGreaterThanOrEqualTo(0d);
        }
    }

    [Test]
    public async Task Business_failure_is_recorded_as_failure_without_marking_the_span_as_error()
    {
        // Arrange
        using var scope = _services.CreateScope();

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RejectedCommand, Unit>>()
            .HandleAsync(new RejectedCommand("c"), CancellationToken.None);

        // Assert
        var span = _capture.SingleSpan("command RejectedCommand");
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("failure");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.ErrorCode)).IsEqualTo("test.rejected");
            await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
            await Assert.That(_capture.SingleMeasurement("RejectedCommand").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("failure");
            await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Information
                && l.Message.Contains("test.rejected", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task Exception_is_recorded_on_the_span_and_rethrown()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExplodingCommand, Unit>>();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(new ExplodingCommand("d"), CancellationToken.None));

        // Assert
        var span = _capture.SingleSpan("command ExplodingCommand");
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Error);
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("exception");
            await Assert.That(span.GetTagItem("error.type")).IsEqualTo(typeof(InvalidOperationException).FullName);
            await Assert.That(_capture.SingleMeasurement("ExplodingCommand").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("exception");
            await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Error)).IsTrue();
        }
    }

    [Test]
    public async Task A_rejection_at_commit_is_answered_as_a_failed_result_and_logged_as_a_rejection_not_an_incident()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RejectedAtCommitCommand, Unit>>();

        // Act
        var result = await handler.HandleAsync(new RejectedAtCommitCommand("r"), CancellationToken.None);

        // Assert
        var span = _capture.SingleSpan("command RejectedAtCommitCommand");
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("test.changed_concurrently");
            await Assert.That(span.Status).IsNotEqualTo(ActivityStatusCode.Error);
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("failure");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.ErrorCode)).IsEqualTo("test.changed_concurrently");
            await Assert.That(_capture.Logs.Any(l => l.Level >= LogLevel.Warning)).IsFalse();
            await Assert.That(_capture.Logs.Any(l => l.Level == LogLevel.Information
                && l.Message.Contains("test.changed_concurrently", StringComparison.Ordinal))).IsTrue();
        }
    }

    [Test]
    public async Task The_gate_sits_inside_the_span_and_outside_the_unit_of_work_Instrumented_Gated_Transactional_handler()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AcceptedCommand, string>>();

        // Act
        var result = await handler.HandleAsync(new AcceptedCommand("g"), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Value).IsEqualTo("accepted:g");
            await Assert.That(string.Join("; ", _gate.Calls)).IsEqualTo(
                "admit AcceptedCommand after 0 unit-of-work calls; complete AcceptedCommand succeeded=True after 2 unit-of-work calls");
            await Assert.That(string.Join(",", _unitOfWork.Calls)).IsEqualTo("begin,commit");
            await Assert.That(_capture.SingleSpan("command AcceptedCommand").GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("success");
        }
    }

    [Test]
    public async Task A_command_the_gate_refuses_never_runs_never_begins_a_unit_of_work_and_is_traced_as_a_failure()
    {
        // Arrange
        _gate.Refusal = new ForbiddenError("test.forbidden");
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<AcceptedCommand, string>>();

        // Act
        var result = await handler.HandleAsync(new AcceptedCommand("h"), CancellationToken.None);

        // Assert
        var span = _capture.SingleSpan("command AcceptedCommand");
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single()).IsEqualTo(new ForbiddenError("test.forbidden"));
            await Assert.That(_unitOfWork.Calls).IsEmpty();
            await Assert.That(string.Join("; ", _gate.Calls)).IsEqualTo("admit AcceptedCommand after 0 unit-of-work calls");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("failure");
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.ErrorCode)).IsEqualTo("test.forbidden");
        }
    }

    [Test]
    public async Task A_query_the_gate_refuses_never_runs_and_a_query_it_admits_is_completed()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>();
        var admitted = await handler.HandleAsync(new Ping("q"), CancellationToken.None);
        _gate.Refusal = new ForbiddenError("test.forbidden");

        // Act
        var refused = await handler.HandleAsync(new Ping("q"), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(admitted.Value).IsEqualTo("pong:q");
            await Assert.That(refused.Errors.Single().Code).IsEqualTo("test.forbidden");
            await Assert.That(string.Join("; ", _gate.Calls)).IsEqualTo(
                "admit Ping after 0 unit-of-work calls; complete Ping succeeded=True after 0 unit-of-work calls; "
                + "admit Ping after 0 unit-of-work calls");
        }
    }

    [Test]
    public async Task Caller_cancellation_is_not_an_incident()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SlowQuery, string>>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var failure = await Assert.ThrowsAsync<TaskCanceledException>(() => handler.HandleAsync(new SlowQuery("e"), cancellation.Token));

        // Assert
        var span = _capture.SingleSpan("query SlowQuery");
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(span.Status).IsEqualTo(ActivityStatusCode.Unset);
            await Assert.That(span.GetTagItem(FrappeTelemetry.Tags.Outcome)).IsEqualTo("cancelled");
            await Assert.That(_capture.SingleMeasurement("SlowQuery").Tags[FrappeTelemetry.Tags.Outcome]).IsEqualTo("cancelled");
            await Assert.That(_capture.Logs.Any(l => l.Level >= LogLevel.Warning)).IsFalse();
        }
    }

    [Test]
    public async Task Registering_the_same_assembly_twice_keeps_one_handler_per_port()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton<IUseCaseGate>(new RecordingUseCaseGate())
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        // Act
        var handlers = scope.ServiceProvider.GetServices<IQueryHandler<Ping, string>>();

        // Assert
        await Assert.That(handlers.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task A_second_handler_for_the_same_port_is_rejected_at_registration()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddScoped<IQueryHandler<Ping, string>, ConflictingPingHandler<int>>();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddHandlers(typeof(HandlerPipelineTests).Assembly));

        // Assert
        await Assert.That(exception.Message).Contains("Ping");
    }

    [Test]
    public async Task Keyed_registrations_of_a_port_are_left_alone()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddKeyedScoped<IQueryHandler<Ping, string>, ConflictingPingHandler<int>>("alternative")
            .AddSingleton<IUseCaseGate>(new RecordingUseCaseGate())
            .AddHandlers(typeof(HandlerPipelineTests).Assembly)
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<Ping, string>>()
            .HandleAsync(new Ping("k"), CancellationToken.None);

        // Assert
        await Assert.That(result.Value).IsEqualTo("pong:k");
    }

    public void Dispose()
    {
        _services.Dispose();
        _capture.Dispose();
    }
}
