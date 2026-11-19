using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>A module database on a real Postgres: its own schema, NodaTime types and its own migrations history.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
public sealed class ModuleDatabaseTests(PostgresServer server)
{
    private const string Schema = "orders";

    [Test]
    public async Task The_migrations_history_table_lives_in_the_module_schema()
    {
        // Arrange
        await using var context = await NewContextAsync();

        // Act
        await context.GetService<IHistoryRepository>().CreateAsync();

        // Assert
        await Assert.That(await TableExistsAsync(Schema, ModuleDatabase.MigrationsHistoryTable)).IsTrue();
        await Assert.That(await TableExistsAsync("public", ModuleDatabase.MigrationsHistoryTable)).IsFalse();
    }

    [Test]
    public async Task An_instant_is_stored_and_read_back_unchanged()
    {
        // Arrange
        var placed = Instant.FromUtc(2030, 1, 2, 3, 4, 5);
        await using (var writer = await NewContextAsync())
        {
            await writer.Database.EnsureCreatedAsync();
            writer.Orders.Add(new Order { Id = 1, PlacedAt = placed });
            await writer.SaveChangesAsync();
        }

        // Act
        await using var reader = await NewContextAsync();
        var order = await reader.Orders.SingleAsync();

        // Assert
        await Assert.That(order.PlacedAt).IsEqualTo(placed);
    }

    private async Task<OrdersContext> NewContextAsync()
    {
        if (_connectionString.Length == 0)
        {
            await CreateDatabaseAsync();
        }

        var options = new DbContextOptionsBuilder<OrdersContext>()
            .UseModulePostgres(ModuleDatabase.WithConnectionLimits(_connectionString, new DatabaseOptions()), Schema)
            .Options;
        return new OrdersContext(options);
    }

    private async Task CreateDatabaseAsync()
    {
        var database = $"module_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(server.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = database }.ConnectionString;
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {Schema}", connection);
            await create.ExecuteNonQueryAsync();
        }
    }

    private string _connectionString = string.Empty;

    private async Task<bool> TableExistsAsync(string schema, string table)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = @s AND table_name = @t", connection);
        command.Parameters.AddWithValue("s", schema);
        command.Parameters.AddWithValue("t", table);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    /// <summary>A row with an instant.</summary>
    private sealed class Order
    {
        public int Id { get; set; }

        public Instant PlacedAt { get; set; }
    }

    /// <summary>A module context over the <c>orders</c> schema.</summary>
    private sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(Schema);
    }
}
