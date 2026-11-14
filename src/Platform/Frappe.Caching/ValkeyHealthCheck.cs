using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Frappe.Caching;

/// <summary>Valkey answers PING. Uses its own connection, opened once, so the check never touches cached data.</summary>
internal sealed class ValkeyHealthCheck(ConfigurationOptions options) : IHealthCheck, IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(() => ConnectionMultiplexer.ConnectAsync(options));

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connection.Value.WaitAsync(cancellationToken);
            await connection.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Valkey does not answer.", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
        {
            await (await _connection.Value).DisposeAsync();
        }
    }
}
