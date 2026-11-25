using Frappe.Caching;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Limits;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.Limits.Valkey;

/// <summary>
/// Counts attempts in the platform's Valkey window counter, shared by every replica, under <c>identity:&lt;flow&gt;:&lt;subject&gt;</c>. One
/// atomic step takes an attempt only while the subject is below its limit. When Valkey is slow or failing (SEC-14): a flow that fails closed
/// answers <see cref="AttemptOutcome.Unavailable"/>, so nothing is judged uncounted; a flow that fails open lets the attempt through and then
/// leaves Valkey alone for <see cref="BreakDuration"/>, so an outage costs one wait and one log line per break, not one per request. Both
/// are counted in <c>frappe.identity.limiter.unavailable</c>.
/// </summary>
internal sealed partial class ValkeyAttemptLimiter(
    ValkeyWindowCounter counter,
    IOptions<AttemptLimitOptions> options,
    IdentityMetrics metrics,
    TimeProvider time,
    ILogger<ValkeyAttemptLimiter> logger)
    : IAttemptLimiter
{
    /// <summary>The longest one attempt waits for Valkey.</summary>
    internal static readonly TimeSpan StoreTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a flow that fails open leaves Valkey alone after a failure.</summary>
    internal static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    // The UTC ticks until which fail-open flows leave Valkey alone; 0 while it is asked.
    private long _openUntilTicks;

    public async Task<AttemptVerdict> HitAsync(LimitFlow flow, string subjectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentException.ThrowIfNullOrEmpty(subjectKey);
        var failsOpen = flow.OnFailure == LimitFailure.FailOpen;
        if (failsOpen && time.GetUtcNow().UtcTicks < Volatile.Read(ref _openUntilTicks))
        {
            return AttemptVerdict.Allowed;
        }

        var (limit, window) = PolicyOf(flow);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StoreTimeout);
        try
        {
            var take = await counter.TryTakeAsync($"identity:{flow.Name}:{subjectKey}", limit, window, timeout.Token);
            return take.Taken ? AttemptVerdict.Allowed : AttemptVerdict.Refused(take.Remaining);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any store failure (connection, timeout, protocol) is answered by the flow's failure mode, never thrown.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return Unavailable(flow, exception);
        }
    }

    private AttemptVerdict Unavailable(LimitFlow flow, Exception exception)
    {
        metrics.LimiterUnavailable(flow.Name);
        if (flow.OnFailure == LimitFailure.FailClosed)
        {
            LogClosed(logger, flow.Name, exception.GetType().Name);
            return AttemptVerdict.Unavailable;
        }

        if (time.GetUtcNow().UtcTicks >= Volatile.Read(ref _openUntilTicks))
        {
            LogOpen(logger, flow.Name, exception.GetType().Name, BreakDuration.TotalSeconds);
        }

        Volatile.Write(ref _openUntilTicks, time.GetUtcNow().Add(BreakDuration).UtcTicks);
        return AttemptVerdict.Allowed;
    }

    // A setting tightens the flow's defaults and never loosens them (AttemptLimitOptions).
    private (int Limit, TimeSpan Window) PolicyOf(LimitFlow flow)
    {
        if (!options.Value.Flows.TryGetValue(flow.Name, out var settings))
        {
            return (flow.Limit, flow.Window);
        }

        var window = TimeSpan.FromTicks(Math.Max(flow.Window.Ticks, (settings.Window ?? flow.Window).Ticks));
        return (Math.Min(flow.Limit, settings.Limit ?? flow.Limit), window);
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The attempt counter failed ({Error}); attempts of '{Flow}' are refused while they cannot be counted")]
    private static partial void LogClosed(ILogger logger, string flow, string error);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The attempt counter failed ({Error}); attempts of '{Flow}' go through uncounted for the next {Seconds} seconds")]
    private static partial void LogOpen(ILogger logger, string flow, string error, double seconds);
}
