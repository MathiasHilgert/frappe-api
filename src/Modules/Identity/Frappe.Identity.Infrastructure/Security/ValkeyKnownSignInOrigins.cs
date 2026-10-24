using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Application.Security;
using Frappe.Platform.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// One Valkey key per email and origin, holding nothing but its expiry. Keys are a SHA-256 of the pair, never the raw
/// email or address. On an outage or a slow call (the limiter's own budget) an origin reads as unknown, which only
/// keeps the per-email ceiling in force, and a lost remember costs the owner nothing but that exemption.
/// </summary>
internal sealed partial class ValkeyKnownSignInOrigins(
    ValkeyConnection valkey,
    IOptions<AttemptLimiterOptions> options,
    IdentityMetrics metrics,
    ILogger<ValkeyKnownSignInOrigins> logger) : IKnownSignInOrigins
{
    private const string KeyPrefix = "frappe:identity:known-origins:";

    public async Task<bool> IsKnownAsync(string email, string clientIp, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        try
        {
            return await (await DatabaseAsync(budget.Token)).KeyExistsAsync(KeyFor(email, clientIp)).WaitAsync(budget.Token);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            Skip(exception);
            return false;
        }
    }

    public async Task RememberAsync(string email, string clientIp, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken);
        try
        {
            await (await DatabaseAsync(budget.Token))
                .StringSetAsync(KeyFor(email, clientIp), 1, IKnownSignInOrigins.KnownFor).WaitAsync(budget.Token);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            Skip(exception);
        }
    }

    internal static RedisKey KeyFor(string email, string clientIp)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(clientIp);
        var pair = $"{email.Trim().ToLowerInvariant()}|{clientIp.Trim().ToLowerInvariant()}";
        return $"{KeyPrefix}{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pair)))}";
    }

    private static bool IsOutage(Exception exception, CancellationToken cancellationToken) =>
        exception is RedisException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private CancellationTokenSource Budget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.Value.Timeout);
        return budget;
    }

    private async Task<IDatabase> DatabaseAsync(CancellationToken cancellationToken) =>
        (await valkey.GetAsync().WaitAsync(cancellationToken)).GetDatabase();

    private void Skip(Exception exception)
    {
        LogUnavailable(exception);
        metrics.KnownOriginsSkipped();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Known sign-in origins unavailable; treating the origin as unknown")]
    private partial void LogUnavailable(Exception exception);
}
