using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>The Notifications migration on a real Postgres, run by the Migrator's phases: its tables, the model it matches and its reruns.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class NotificationsMigrationTests(PostgresServer server)
{
    [Test]
    public async Task The_migration_creates_the_module_schema_with_its_tables_and_its_outbox_and_inbox()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var tables = await TablesAsync(owner);

            // Assert
            await Assert.That(tables).IsEquivalentTo(
                [
                    "__ef_migrations_history", "deliveries", "inbox_messages", "outbox_messages", "preferences", "provider_events", "send_requests",
                    "suppressions",
                ]);
        });
    }

    [Test]
    public async Task The_migrated_database_matches_the_model_and_a_rerun_changes_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var context = new NotificationsDbContext(
                ModuleDatabase.Options<NotificationsDbContext>(owner, NotificationsDbContext.Schema));
            var appliedFirst = (await context.Database.GetAppliedMigrationsAsync()).ToList();

            // Act
            await MigratedNotifications.MigrateAsync(owner);
            var pending = await context.Database.GetPendingMigrationsAsync();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(pending).IsEmpty();
                await Assert.That(applied).IsEquivalentTo(appliedFirst);
                await Assert.That(applied).IsNotEmpty();
                await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
            }
        });
    }

    [Test]
    public async Task The_idempotency_key_is_unique_with_nulls_not_distinct_and_the_provider_message_id_is_unique_only_when_present()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var idempotency = (string)(await ScratchDatabase.ScalarAsync(
                owner, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_deliveries_message_kind_recipient_channel'"))!;
            var provider = (string)(await ScratchDatabase.ScalarAsync(
                owner, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_deliveries_provider_message'"))!;

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(idempotency).Contains("UNIQUE")
                    .And.Contains("(source_message_id, kind, recipient_key, channel) NULLS NOT DISTINCT");
                await Assert.That(provider).Contains("UNIQUE").And.Contains("WHERE (provider_message_id IS NOT NULL)");
            }
        });
    }

    [Test]
    public async Task The_purge_indexes_exist_on_deliveries_and_provider_events()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Act
            var indexes = await NamesAsync(owner, "SELECT indexname FROM pg_indexes WHERE schemaname = 'notifications'");

            // Assert
            await Assert.That(indexes).Contains("ix_deliveries_created_at").And.Contains("ix_provider_events_received_at");
        });
    }

    [Test]
    public async Task A_delivery_written_as_the_owner_is_read_back_through_the_model_with_its_closed_sets_as_snake_case_text()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var id = Guid.CreateVersion7();
            await ScratchDatabase.ExecuteAsync(owner, $"""
                INSERT INTO notifications.deliveries (id, source_message_id, kind, category, channel, recipient_key, locale_served,
                  status, provider, provider_message_id, created_at, sent_at, status_changed_at)
                VALUES ('{id}', '{Guid.CreateVersion7()}', 'account_activation_code', 'security', 'email', 'ref:principal:42',
                  'es-AR', 'sent', 'resend', 're_123', now(), now(), now())
                """);

            // Act
            await using var context = new NotificationsDbContext(
                ModuleDatabase.Options<NotificationsDbContext>(owner, NotificationsDbContext.Schema));
            var delivery = await context.Deliveries.SingleAsync();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(delivery.Id).IsEqualTo(id);
                await Assert.That(delivery.Kind).IsEqualTo("account_activation_code");
                await Assert.That(delivery.Category).IsEqualTo(NotificationCategory.Security);
                await Assert.That(delivery.Status).IsEqualTo(DeliveryStatus.Sent);
                await Assert.That(delivery.Provider).IsEqualTo(DeliveryProvider.Resend);
                await Assert.That(delivery.LocaleServed?.Value).IsEqualTo("es-AR");
                await Assert.That(delivery.FailureReason).IsNull();
                await Assert.That(delivery.AddressHash).IsNull();
            }
        });
    }

    private static Task<string[]> TablesAsync(string owner) =>
        NamesAsync(owner, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'notifications'");

    private static async Task<string[]> NamesAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        List<string> names = [];
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }
}
