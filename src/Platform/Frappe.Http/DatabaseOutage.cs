using System.Data.Common;

namespace Frappe.Http;

/// <summary>
/// Whether an exception means the database did not answer: a connection that could not be opened or was lost, a command
/// that timed out, a server shutting down or out of connections, or a transaction it asked to retry. Any of them, raised
/// directly or wrapped by EF Core (<c>DbUpdateException</c>, the "transient failure" <see cref="InvalidOperationException"/>,
/// <c>RetryLimitExceededException</c>), is an outage the client may wait out. The provider decides what is transient
/// (<see cref="DbException.IsTransient"/>; Npgsql marks connection, timeout and SQLSTATE classes 08, 40001, 40P01, 53,
/// 57P0x and similar), so a refusal (a constraint, a missing table) never counts: it is a failure of the request. One
/// non-transient state counts too: <c>57014</c> (<c>query_canceled</c>), what the server raises when a statement runs
/// past the role's <c>statement_timeout</c>. That limit stands in for the client command timeout the Api answered with a
/// <c>503</c> before, and a statement that slow means the database is not answering in time.
/// Only the base class library is read, so the HTTP platform knows no database provider.
/// </summary>
internal static class DatabaseOutage
{
    /// <summary>SQLSTATE <c>query_canceled</c>, raised for a statement cut by <c>statement_timeout</c>.</summary>
    private const string StatementTimeoutSqlState = "57014";

    public static bool Caused(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { IsTransient: true } or DbException { SqlState: StatementTimeoutSqlState })
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                return aggregate.InnerExceptions.Any(Caused);
            }
        }

        return false;
    }
}
