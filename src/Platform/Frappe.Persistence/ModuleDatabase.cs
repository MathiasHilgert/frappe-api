using System.Data.Common;
using Frappe.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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

    extension(ModelConfigurationBuilder configuration)
    {
        /// <summary>Stores a <see cref="LanguageTag"/> as its normalized BCP 47 text.</summary>
        public ModelConfigurationBuilder StoreLanguageTags()
        {
            ArgumentNullException.ThrowIfNull(configuration);
            configuration.Properties<LanguageTag>().HaveConversion<LanguageTagConverter>().HaveMaxLength(LanguageTag.StorageLength);
            return configuration;
        }
    }

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// The options of <typeparamref name="TDbContext"/> outside the container (the Migrator's, <c>dotnet ef</c>'s design-time
    /// factories): the same Postgres, NodaTime and schema the host builds it with, plus what <paramref name="configure"/> adds.
    /// </summary>
    public static DbContextOptions<TDbContext> Options<TDbContext>(string connectionString, string schema,
        Action<DbContextOptionsBuilder>? configure = null)
        where TDbContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TDbContext>().UseModulePostgres(connectionString, schema);
        configure?.Invoke(builder);
        return builder.Options;
    }

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

    /// <summary>Stores a language tag as its text and reads it back validated.</summary>
    private sealed class LanguageTagConverter() : ValueConverter<LanguageTag, string>(
        tag => tag.Value,
        value => LanguageTag.Create(value).Value);
}
