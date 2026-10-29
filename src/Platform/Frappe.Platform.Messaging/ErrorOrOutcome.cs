using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.Runtime;

namespace Frappe.Platform.Messaging;

/// <summary>
/// Settles the <see cref="ErrorOr{TValue}"/> a handler returns. Public only because generated handler code
/// calls it.
/// </summary>
public sealed class ErrorOrOutcome
{
    /// <summary>The meter the messaging adapter's own instruments belong to.</summary>
    public const string MeterName = "Frappe.Platform.Messaging";

    /// <summary>The span attribute that carries the codes of a handler's expected errors, comma-separated.</summary>
    public const string ErrorCodeTag = "frappe.error.code";

    private readonly ILogger<ErrorOrOutcome> _logger;

    // Cardinality: one series per message type, a closed set of types.
    private readonly Counter<long> _rejections;

    /// <summary>Initializes the outcome with its log and meter.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="meters">The meter factory.</param>
    public ErrorOrOutcome(ILogger<ErrorOrOutcome> logger, IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _logger = logger;
        _rejections = meters
            .Create(MeterName)
            .CreateCounter<long>(
                "frappe.messaging.rejections",
                "{message}",
                "Messages whose handler returned expected errors and were acknowledged without retry"
            );
    }

    /// <summary>
    /// Hands the result back to an inline invoke; an error also discards everything the handler queued and
    /// rolls its transaction back, and outside an invoke it is logged and counted, then acknowledged.
    /// </summary>
    /// <param name="context">The context of the message being handled.</param>
    /// <param name="result">The handler's result.</param>
    /// <returns><see langword="true"/> when the result is an error, so nothing after the handler commits.</returns>
    public async Task<bool> SettleAsync(MessageContext context, IErrorOr result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);
        bool invoked = IsInlineInvoke(context.Envelope);
        if (invoked)
        {
            await context.EnqueueCascadingAsync(result);
        }

        if (!result.IsError)
        {
            return false;
        }

        await context.ClearAllAsync();
        string codes = string.Join(',', result.Errors?.Select(static error => error.Code) ?? []);
        // An expected error is not a failure: Wolverine closes the message's span Ok, and the tag says which one.
        Activity.Current?.SetTag(ErrorCodeTag, codes);
        if (!invoked)
        {
            string messageType = context.Envelope?.MessageType ?? "unknown";
            Log.Rejected(_logger, messageType, codes);
            _rejections.Add(
                1,
                new KeyValuePair<string, object?>("messaging.message.type", messageType)
            );
        }

        return true;
    }

    // Only InvokeAsync<T> sets this flag, and it then stores the result as the response instead of
    // cascading it; read here alone, so an upgrade that changes its meaning breaks one tested place.
    internal static bool IsInlineInvoke(Envelope? envelope) =>
        envelope?.DoNotCascadeResponse == true;
}
