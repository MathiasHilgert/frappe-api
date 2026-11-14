using Frappe.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Frappe.Persistence;

/// <summary>
/// Conventions every module database follows: Postgres, NodaTime types, and a schema owned by the module
/// that also holds the module's own migrations history, so each module migrates on its own.
/// </summary>
public static class ModuleDatabase
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static TBuilder UseModulePostgres<TBuilder>(
        this TBuilder builder,
        string connectionString,
        string schema)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        builder.UseNpgsql(connectionString, npgsql => npgsql
            .UseNodaTime()
            .MigrationsHistoryTable(MigrationsHistoryTable, schema));
        return builder;
    }

    /// <summary>Stores a <see cref="LanguageTag"/> as its normalized BCP 47 text.</summary>
    public static ModelConfigurationBuilder StoreLanguageTags(this ModelConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Properties<LanguageTag>().HaveConversion<LanguageTagConverter>().HaveMaxLength(35);
        return configuration;
    }

    private sealed class LanguageTagConverter() : ValueConverter<LanguageTag, string>(
        tag => tag.Value,
        value => LanguageTag.Create(value).Value);
}
