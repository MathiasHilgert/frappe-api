using Frappe.Persistence;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>
/// A login role the Migrator sets up runs every session under a statement timeout and an idle-in-transaction timeout,
/// whichever client connects with it.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class RoleSessionLimitsTests(PostgresServer server)
{
    private const string Password = "role-limits-test-password";

    [Test]
    public async Task A_session_of_the_role_runs_under_both_timeouts()
    {
        // Act
        var role = await LoginRoleAsync(new DatabaseOptions());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(await ShowAsync(role, "statement_timeout")).IsEqualTo("30s");
            await Assert.That(await ShowAsync(role, "idle_in_transaction_session_timeout")).IsEqualTo("1min");
        }
    }

    [Test]
    public async Task Setting_the_limits_again_replaces_them()
    {
        // Act
        var role = await LoginRoleAsync(new DatabaseOptions());

        await LimitAsync(role, new DatabaseOptions
        {
            StatementTimeout = TimeSpan.FromSeconds(45),
            IdleInTransactionSessionTimeout = TimeSpan.FromMinutes(2),
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(await ShowAsync(role, "statement_timeout")).IsEqualTo("45s");
            await Assert.That(await ShowAsync(role, "idle_in_transaction_session_timeout")).IsEqualTo("2min");
        }
    }

    [Test]
    public async Task A_statement_longer_than_the_limit_is_cancelled_by_the_server()
    {
        // Arrange
        var role = await LoginRoleAsync(new DatabaseOptions { StatementTimeout = TimeSpan.FromSeconds(1) });

        await using var connection = new NpgsqlConnection(RoleConnectionString(role));
        await connection.OpenAsync();
        await using var sleep = new NpgsqlCommand("SELECT pg_sleep(5)", connection);

        // Act
        var failure = await Assert.ThrowsAsync<PostgresException>(() => sleep.ExecuteNonQueryAsync());

        // Assert
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.QueryCanceled);
    }

    private async Task<string> LoginRoleAsync(DatabaseOptions limits)
    {
        var role = "limits_" + Guid.NewGuid().ToString("N")[..12];
        await using var owner = new NpgsqlConnection(server.ConnectionString);
        await owner.OpenAsync();
        await ModuleRoles.EnsureLoginRoleAsync(owner, role, Password, CancellationToken.None);
        await ModuleRoles.LimitSessionsAsync(owner, role, limits, CancellationToken.None);
        return role;
    }

    private async Task LimitAsync(string role, DatabaseOptions limits)
    {
        await using var owner = new NpgsqlConnection(server.ConnectionString);
        await owner.OpenAsync();
        await ModuleRoles.LimitSessionsAsync(owner, role, limits, CancellationToken.None);
    }

    private async Task<string> ShowAsync(string role, string setting)
    {
        await using var connection = new NpgsqlConnection(RoleConnectionString(role));
        await connection.OpenAsync();
        await using var show = new NpgsqlCommand($"SHOW {setting}", connection);
        return (string)(await show.ExecuteScalarAsync())!;
    }

    /// <summary>Unpooled: every check opens a fresh session, the only kind a changed role setting reaches.</summary>
    private string RoleConnectionString(string role) =>
        new NpgsqlConnectionStringBuilder(server.ConnectionString) { Username = role, Password = Password, Pooling = false }.ConnectionString;
}
