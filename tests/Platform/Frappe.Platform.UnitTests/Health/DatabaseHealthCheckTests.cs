using System.Data;
using System.Data.Common;
using Frappe.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>A database that cannot be reached reports the registration's failure status without leaking the cause.</summary>
public sealed class DatabaseHealthCheckTests
{
    [Test]
    public async Task A_connection_that_cannot_be_created_fails_with_the_registration_failure_status()
    {
        // Arrange
        var check = new DatabaseHealthCheck((Func<DbConnection>)(() => throw new InvalidOperationException("secret-host:5432")));
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", check, HealthStatus.Degraded, null),
        };

        // Act
        var result = await check.CheckHealthAsync(context);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(result.Description).IsEqualTo("The database does not answer.");
        }
    }

    [Test]
    public async Task A_reachable_database_is_healthy_after_answering_select_1()
    {
        // Arrange
        var connection = new FakeConnection();
        var check = new DatabaseHealthCheck(() => connection);

        // Act
        var result = await check.CheckHealthAsync(ContextOf(check));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(connection.Executed).IsEqualTo("SELECT 1");
        }
    }

    [Test]
    public async Task A_database_error_while_opening_fails_with_the_registration_failure_status()
    {
        // Arrange
        var check = new DatabaseHealthCheck(() => new FakeConnection { FailOpen = true });

        // Act
        var result = await check.CheckHealthAsync(ContextOf(check));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
            await Assert.That(result.Description).IsEqualTo("The database does not answer.");
        }
    }

    private static HealthCheckContext ContextOf(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration("database", check, HealthStatus.Degraded, null),
    };

    /// <summary>A database error the fake connection throws when it fails to open.</summary>
    private sealed class FakeException : DbException;

    /// <summary>A connection that opens without a server, or fails to, and records the command it executed.</summary>
    private sealed class FakeConnection : DbConnection
    {
        public bool FailOpen { get; init; }

        public string? Executed { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "fake";

        public override string DataSource => "fake";

        public override string ServerVersion => "1";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override Task OpenAsync(CancellationToken cancellationToken) =>
            FailOpen ? throw new FakeException() : Task.CompletedTask;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);
    }

    /// <summary>A command that records its text on its connection and answers 1.</summary>
    private sealed class FakeCommand(FakeConnection owner) : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object? ExecuteScalar()
        {
            owner.Executed = CommandText;
            return 1;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }
}
