using System.Diagnostics;
using Frappe.Results;
using Microsoft.Extensions.Logging;

namespace Frappe.Observability;

/// <summary>
/// Wraps every handler with a span, a duration measurement and structured logs, so modules contain no telemetry code.
/// Business failures (<c>failure</c>) and caller cancellations (<c>cancelled</c>) do not mark the span as an error;
/// only exceptions do, because only exceptions are incidents.
/// </summary>
internal static partial class HandlerInstrumentation
{
    public static async Task<Result<TResult>> ExecuteAsync<TRequest, TResult>(
        string kind,
        HandlerMetrics metrics,
        ILogger logger,
        Func<Task<Result<TResult>>> handle,
        CancellationToken cancellationToken)
    {
        var useCase = typeof(TRequest).Name;
        var module = HandlerModule<TRequest>.Name;
        var startedAt = Stopwatch.GetTimestamp();

        using var activity = FrappeTelemetry.Source.StartActivity($"{kind} {useCase}", ActivityKind.Internal);
        activity?.SetTag(FrappeTelemetry.Tags.Kind, kind);
        activity?.SetTag(FrappeTelemetry.Tags.UseCase, useCase);
        activity?.SetTag(FrappeTelemetry.Tags.Module, module);

        var outcome = "exception";
        try
        {
            var result = await handle();
            if (result.IsSuccess)
            {
                outcome = "success";
                LogSucceeded(logger, kind, useCase);
            }
            else
            {
                outcome = "failure";
                activity?.SetTag(FrappeTelemetry.Tags.ErrorCode, result.Errors[0].Code);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    var codes = string.Join(", ", result.Errors.Select(e => e.Code));
                    LogRejected(logger, kind, useCase, codes);
                }
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
            RecordIncident(exception, activity, logger, kind, useCase);
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
    /// An exception escaping the use case is an incident (a commit rejection never gets here: the transactional step answers it
    /// as a failed Result, CQRS-05).
    /// </summary>
    private static void RecordIncident(Exception exception, Activity? activity, ILogger logger, string kind, string useCase)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.AddException(exception);
        LogFailed(logger, exception, kind, useCase);
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
