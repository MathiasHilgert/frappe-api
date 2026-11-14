using System.Diagnostics;
using System.Diagnostics.Metrics;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.Logging;

namespace Frappe.Observability;

/// <summary>Holds the platform instruments, created once through <see cref="IMeterFactory"/>.</summary>
public sealed class HandlerMetrics
{
    public HandlerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(FrappeTelemetry.Name);
        Duration = meter.CreateHistogram<double>(
            FrappeTelemetry.HandlerDurationMetric,
            unit: "s",
            description: "Duration of command and query handlers.");
    }

    internal Histogram<double> Duration { get; }
}

/// <summary>
/// Wraps every handler with a span, a duration measurement and structured logs, so modules contain no telemetry code.
/// Business failures (<c>failure</c>) and caller cancellations (<c>cancelled</c>) do not mark the span as an error;
/// only exceptions do, because only exceptions are incidents.
/// </summary>
internal static partial class HandlerInstrumentation
{
    public static async Task<Result<TResult>> ExecuteAsync<TResult>(
        string kind,
        Type requestType,
        HandlerMetrics metrics,
        ILogger logger,
        Func<Task<Result<TResult>>> handle,
        CancellationToken cancellationToken)
    {
        var useCase = requestType.Name;
        var module = HandlerModule.Of(requestType);
        var startedAt = Stopwatch.GetTimestamp();

        using var activity = FrappeTelemetry.Source.StartActivity($"{kind} {useCase}", ActivityKind.Internal);
        activity?.SetTag(FrappeTelemetry.Tags.Kind, kind);
        activity?.SetTag(FrappeTelemetry.Tags.UseCase, useCase);
        activity?.SetTag(FrappeTelemetry.Tags.Module, module);

        var outcome = "exception";
        try
        {
            var result = await handle().ConfigureAwait(false);
            if (result.IsSuccess)
            {
                outcome = "success";
                LogSucceeded(logger, kind, useCase);
            }
            else
            {
                outcome = "failure";
                var codes = string.Join(", ", result.Errors.Select(e => e.Code));
                activity?.SetTag(FrappeTelemetry.Tags.ErrorCode, result.Errors[0].Code);
                LogRejected(logger, kind, useCase, codes);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            LogCancelled(logger, kind, useCase);
            throw;
        }
        catch (Exception exception)
        {
            if (IsRejection(exception, activity, logger, kind, useCase))
            {
                outcome = "failure";
            }

            throw;
        }
        finally
        {
            activity?.SetTag(FrappeTelemetry.Tags.Outcome, outcome);
            metrics.Duration.Record(
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
                new KeyValuePair<string, object?>(FrappeTelemetry.Tags.Kind, kind),
                new KeyValuePair<string, object?>(FrappeTelemetry.Tags.UseCase, useCase),
                new KeyValuePair<string, object?>(FrappeTelemetry.Tags.Module, module),
                new KeyValuePair<string, object?>(FrappeTelemetry.Tags.Outcome, outcome));
        }
    }

    /// <summary>
    /// An exception escaping the use case: a rejection when the storage refused the changes for an expected reason
    /// (someone else changed them first), an incident otherwise. Records it and says whether it was a rejection.
    /// </summary>
    private static bool IsRejection(Exception exception, Activity? activity, ILogger logger, string kind, string useCase)
    {
        if (exception is ICommitRejection rejection)
        {
            activity?.SetTag(FrappeTelemetry.Tags.ErrorCode, rejection.Answer.Code);
            LogRejected(logger, kind, useCase, rejection.Answer.Code);
            return true;
        }

        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.AddException(exception);
        LogFailed(logger, exception, kind, useCase);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Kind} {UseCase} succeeded")]
    private static partial void LogSucceeded(ILogger logger, string kind, string useCase);

    // Information, not Warning: a business rule saying "no" is normal traffic, not something an operator must look at.
    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {UseCase} was rejected: {ErrorCodes}")]
    private static partial void LogRejected(ILogger logger, string kind, string useCase, string errorCodes);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Kind} {UseCase} was cancelled by the caller")]
    private static partial void LogCancelled(ILogger logger, string kind, string useCase);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Kind} {UseCase} failed with an exception")]
    private static partial void LogFailed(ILogger logger, Exception exception, string kind, string useCase);
}

internal sealed class InstrumentedCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    HandlerMetrics metrics,
    ILogger<InstrumentedCommandHandler<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        HandlerInstrumentation.ExecuteAsync(
            "command",
            typeof(TCommand),
            metrics,
            logger,
            () => inner.HandleAsync(command, cancellationToken),
            cancellationToken);
}

internal sealed class InstrumentedQueryHandler<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    HandlerMetrics metrics,
    ILogger<InstrumentedQueryHandler<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        HandlerInstrumentation.ExecuteAsync(
            "query",
            typeof(TQuery),
            metrics,
            logger,
            () => inner.HandleAsync(query, cancellationToken),
            cancellationToken);
}
