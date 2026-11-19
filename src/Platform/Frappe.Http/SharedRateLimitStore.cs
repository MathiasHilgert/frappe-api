using Microsoft.Extensions.Logging;

namespace Frappe.Http;

/// <summary>
/// The <see cref="IRateLimitStore"/> as the limiters use it: each call waits at most <see cref="Timeout"/>, and a failure
/// or timeout stops asking the store for <see cref="BreakDuration"/>, after which a single request probes it again. An
/// outage costs at most <see cref="Timeout"/> to the requests already waiting when it starts, and then to one probe per
/// break, not to every request. Until the store answers, the answer is <see langword="null"/> and the limiter counts on
/// its own. A request aborted by its caller is not a store failure. Every answer and every request counted locally is
/// measured (<see cref="RateLimitMetrics"/>).
/// </summary>
internal sealed partial class SharedRateLimitStore(
    IRateLimitStore store,
    TimeProvider time,
    RateLimitMetrics metrics,
    ILogger<SharedRateLimitStore> logger)
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    // 0 while closed; once opened, when the break ends. Past it, one probe (_probing) decides whether it closes.
    private long _openUntilTicks;
    private int _probing;

    /// <summary>The clock the timeout, the breaker and the limiters' idle time run on.</summary>
    public TimeProvider Time => time;

    /// <summary>Whether the store is skipped for now after a failure.</summary>
    public bool IsOpen => time.GetUtcNow().UtcTicks < Volatile.Read(ref _openUntilTicks);

    public async ValueTask<RateLimitWindow?> TryHitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken)
    {
        var openUntil = Volatile.Read(ref _openUntilTicks);
        var probing = false;
        if (openUntil != 0)
        {
            // Open, or just past its break: then one request probes the store while the others keep counting locally.
            if (time.GetUtcNow().UtcTicks < openUntil || Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
            {
                metrics.CountedLocally(RateLimitMetrics.Skipped);
                return null;
            }

            probing = true;
        }

        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = time.GetTimestamp();
        try
        {
            var answer = await store.HitAsync(key, permits, window, linked.Token);
            metrics.StoreAnswered(time.GetElapsedTime(started), ok: true);
            if (probing)
            {
                Volatile.Write(ref _openUntilTicks, 0);
            }

            return answer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any store failure (connection, timeout, protocol) must fall back, never fail the request.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            metrics.StoreAnswered(time.GetElapsedTime(started), ok: false);
            metrics.CountedLocally(RateLimitMetrics.Failed);
            Open(exception);
            return null;
        }
        finally
        {
            if (probing)
            {
                Volatile.Write(ref _probing, 0);
            }
        }
    }

    /// <summary>Opens the breaker unless it already is, so a burst of failing requests logs once.</summary>
    private void Open(Exception exception)
    {
        var now = time.GetUtcNow();
        var current = Volatile.Read(ref _openUntilTicks);
        if (current > now.UtcTicks)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _openUntilTicks, now.Add(BreakDuration).UtcTicks, current) == current)
        {
            LogStoreUnavailable(logger, exception, BreakDuration.TotalSeconds);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shared rate limit store failed; each replica counts on its own for the next {Seconds} seconds")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception, double seconds);
}
