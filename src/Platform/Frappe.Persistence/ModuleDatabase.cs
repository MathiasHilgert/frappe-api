using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>
/// Conventions every module database follows: Postgres, NodaTime types, and a schema owned by the module
/// that also holds the module's own migrations history, so each module migrates on its own.
/// </summary>
public static class ModuleDatabase
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : DbContextOptionsBuilder
    {
        public TBuilder UseModulePostgres(string connectionString,
            string schema)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(schema);

            builder.UseNpgsql(connectionString, npgsql => npgsql
                .UseNodaTime()
                .MigrationsHistoryTable(MigrationsHistoryTable, schema));
            return builder;
        }
    }

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// <paramref name="connectionString"/> under the limits of <paramref name="limits"/>: Npgsql's <c>Maximum Pool Size</c>
    /// and <c>Command Timeout</c>, each only when the connection string does not set it itself, so an operator can still
    /// size one database apart from the others. Without it every connection string gets Npgsql's 100 connections per
    /// process, and a few replicas exceed what Postgres accepts. A <c>Minimum Pool Size</c> above the maximum raises the
    /// maximum to it rather than failing on the first connection.
    /// </summary>
    public static string WithConnectionLimits(string connectionString, DatabaseOptions limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(limits);

        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // The base class lists only the keywords the string sets, under their canonical names (MaxPoolSize included);
        // Npgsql's own ContainsKey is true for every keyword it knows.
        var written = ((DbConnectionStringBuilder)builder).Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!written.Contains(MaxPoolSizeKeyword))
        {
            builder.MaxPoolSize = Math.Max(limits.MaxPoolSize, builder.MinPoolSize);
        }

        if (!written.Contains(CommandTimeoutKeyword))
        {
            builder.CommandTimeout = (int)limits.CommandTimeout.TotalSeconds;
        }

        return builder.ConnectionString;
    }

    private const string MaxPoolSizeKeyword = "Maximum Pool Size";

    private const string CommandTimeoutKeyword = "Command Timeout";
}
