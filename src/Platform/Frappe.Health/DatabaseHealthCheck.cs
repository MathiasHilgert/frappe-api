using System.Data.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Health;

/// <summary>A database answers <c>SELECT 1</c> with the same credentials the process uses.</summary>
public sealed class DatabaseHealthCheck(Func<DbConnection> connect) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = connect();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is DbException or TimeoutException or InvalidOperationException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database does not answer.", exception);
        }
    }
}
