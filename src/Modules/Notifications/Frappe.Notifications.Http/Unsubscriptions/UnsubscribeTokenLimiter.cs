using System.Security.Cryptography;
using System.Text;
using Frappe.Http;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.Http.Unsubscriptions;

/// <summary>
/// The limit per unsubscribe token (S5, SEC-13): one link is not hammered, whatever addresses the calls come from. It counts in the platform's
/// shared <see cref="IRateLimitStore"/> under a hash of the token, never the token. It slows enumeration only, so it fails open (SEC-14): a
/// store that is missing, slow or failing lets the call through, and the per-client policy of the route still counts it. After a failure it
/// leaves the store alone for <see cref="BreakDuration"/>, so an outage costs one wait and one log line per break, not one per call.
/// </summary>
internal sealed partial class UnsubscribeTokenLimiter(ILogger<UnsubscribeTokenLimiter> logger, TimeProvider time, IRateLimitStore? store = null)
{
    /// <summary>Calls per token per <see cref="Window"/>.</summary>
    internal const int PermitLimit = 10;

    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    internal static readonly TimeSpan StoreTimeout = TimeSpan.FromMilliseconds(250);

    internal static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);

    // The UTC ticks until which the store is left alone; 0 while it is asked.
    private long _openUntilTicks;

    /// <summary>How long to wait before the token may be used again, or <see langword="null"/> when the call may go on.</summary>
    public async Task<TimeSpan?> RetryAfterAsync(string? token, CancellationToken cancellationToken)
    {
        if (store is null || string.IsNullOrEmpty(token) || time.GetUtcNow().UtcTicks < Volatile.Read(ref _openUntilTicks))
        {
            return null;
        }

        var key = "notifications:unsubscribe:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StoreTimeout);
        try
        {
            var window = await store.HitAsync(key, 1, Window, timeout.Token);
            return window.Count > PermitLimit ? window.Remaining : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Any store failure (connection, timeout, protocol) lets the call through: this limit fails open.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (time.GetUtcNow().UtcTicks >= Volatile.Read(ref _openUntilTicks))
            {
                LogStoreFailed(logger, exception.GetType().Name, BreakDuration.TotalSeconds);
            }

            Volatile.Write(ref _openUntilTicks, time.GetUtcNow().Add(BreakDuration).UtcTicks);
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The rate limit store failed ({Error}); unsubscribes go through uncounted by token for the next {Seconds} seconds")]
    private static partial void LogStoreFailed(ILogger logger, string error, double seconds);
}
