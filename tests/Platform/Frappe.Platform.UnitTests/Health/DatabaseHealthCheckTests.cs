using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Frappe.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.UnitTests.Health;

/// <summary>The database check: <c>SELECT 1</c> answers, or the registration's failure status is reported.</summary>
public sealed class DatabaseHealthCheckTests
{
    /// <summary>A database failure with a text.</summary>
    private sealed class FakeDbException(string message) : DbException(message);

    /// <summary>A connection that opens, or fails to open with the given failure, and records what it ran.</summary>
    private sealed class FakeConnection(Exception? failure) : DbConnection
    {
        public string? Executed { get; private set; }

        public bool WasDisposed { get; private set; }

        [AllowNull]
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

        public override void Open() => _ = failure is null ? 0 : throw failure;

        public void Record(string text) => Executed = text;

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new FakeCommand(this);

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>A command that records its text on the connection and answers 1.</summary>
    private sealed class FakeCommand(FakeConnection owner) : DbCommand
    {
        [AllowNull]
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

        public override object ExecuteScalar()
        {
            owner.Record(CommandText);
            return 1;
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("database", _ => new DatabaseHealthCheck(() => null!), HealthStatus.Unhealthy, null),
    };

    [Test]
    public async Task A_database_that_answers_is_healthy_and_the_connection_is_released()
    {
        // Arrange
        var connection = new FakeConnection(null);

        // Act
        var result = await new DatabaseHealthCheck(() => connection).CheckHealthAsync(Context());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
            await Assert.That(connection.Executed).IsEqualTo("SELECT 1");
            await Assert.That(connection.WasDisposed).IsTrue();
        }
    }

    [Test]
    public async Task A_database_that_does_not_answer_reports_the_failure_status_without_leaking_the_cause_in_the_text()
    {
        // Arrange
        Exception[] causes = [new FakeDbException("secret-host:5432 refused"), new TimeoutException(), new InvalidOperationException()];

        // Act
        var results = new List<HealthCheckResult>();
        foreach (var cause in causes)
        {
            results.Add(await new DatabaseHealthCheck(() => new FakeConnection(cause)).CheckHealthAsync(Context()));
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(results.Select(r => r.Status).Distinct()).IsEquivalentTo([HealthStatus.Unhealthy]);
            await Assert.That(results.Select(r => r.Description!).Distinct()).IsEquivalentTo(["The database does not answer."]);
            await Assert.That(results.Select(r => r.Exception!)).IsEquivalentTo(causes);
        }
    }
}
