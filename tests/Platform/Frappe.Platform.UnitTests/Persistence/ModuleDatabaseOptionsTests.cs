using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>
/// The options a module context is built with outside the container (the Migrator, <c>dotnet ef</c>) are the ones the
/// host builds it with: Postgres, NodaTime and the module's schema (<see cref="ModuleDatabase.Options{TDbContext}"/>).
/// </summary>
public sealed class ModuleDatabaseOptionsTests
{
    private const string Connection = "Host=localhost;Database=frappe;Username=design_time";

    [Test]
    public async Task The_options_connect_to_postgres_and_keep_the_migrations_history_in_the_schema()
    {
        // Act
        var options = ModuleDatabase.Options<OrdersContext>(Connection, "orders");

        // Assert
        var npgsql = options.Extensions.OfType<RelationalOptionsExtension>().Single();
        using (Assert.Multiple())
        {
            await Assert.That(npgsql.ConnectionString).IsEqualTo(Connection);
            await Assert.That(npgsql.MigrationsHistoryTableName).IsEqualTo(ModuleDatabase.MigrationsHistoryTable);
            await Assert.That(npgsql.MigrationsHistoryTableSchema).IsEqualTo("orders");
        }
    }

    [Test]
    public async Task The_callback_adds_to_the_options()
    {
        // Act
        var options = ModuleDatabase.Options<OrdersContext>(Connection, "orders", builder => builder.EnableSensitiveDataLogging());

        // Assert
        await Assert.That(options.FindExtension<CoreOptionsExtension>()!.IsSensitiveDataLoggingEnabled).IsTrue();
    }

    [Test]
    public async Task The_options_build_a_context_of_the_module()
    {
        // Act
        await using var context = new OrdersContext(ModuleDatabase.Options<OrdersContext>(Connection, "orders"));

        // Assert
        await Assert.That(context.Database.IsNpgsql()).IsTrue();
    }

    [Test]
    [Arguments("", "orders")]
    [Arguments("  ", "orders")]
    [Arguments(Connection, "")]
    public async Task A_connection_string_and_a_schema_are_required(string connection, string schema)
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => ModuleDatabase.Options<OrdersContext>(connection, schema));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo(connection.Trim().Length == 0 ? "connectionString" : "schema");
    }

    /// <summary>A module context built outside the container.</summary>
    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options);
}
