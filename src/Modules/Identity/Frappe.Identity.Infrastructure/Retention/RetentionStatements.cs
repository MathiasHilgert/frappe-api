using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;
using Npgsql;
using NpgsqlTypes;

namespace Frappe.Identity.Infrastructure.Retention;

/// <summary>
/// Runs the retention's own SQL on a module context: statements with a <c>RETURNING</c> that EF cannot compose, inside the transaction the
/// context has open (if any). Every value is a parameter; the tables and columns are constants of the statements (SEC-01).
/// </summary>
internal static class RetentionStatements
{
    /// <summary>Runs <paramref name="sql"/> with the cutoff as <c>$1</c> and the batch as <c>$2</c>, and reads each row it returns.</summary>
    public static async Task<List<T>> QueryAsync<T>(
        DbContext db, string sql, Instant cutoff, int batchSize, Func<DbDataReader, T> read, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
            command.Parameters.Add(new NpgsqlParameter { Value = cutoff, NpgsqlDbType = NpgsqlDbType.TimestampTz });
            command.Parameters.Add(new NpgsqlParameter { Value = batchSize, NpgsqlDbType = NpgsqlDbType.Integer });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<T>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
