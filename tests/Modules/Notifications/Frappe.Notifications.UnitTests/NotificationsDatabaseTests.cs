using Frappe.Notifications.Infrastructure.Persistence;

namespace Frappe.Notifications.UnitTests;

/// <summary>What the Migrator is told about Notifications: its schema and its two login roles, without a database.</summary>
public sealed class NotificationsDatabaseTests
{
    [Test]
    public async Task It_owns_the_notifications_schema_and_names_the_two_login_roles_with_their_password_keys()
    {
        // Arrange
        var database = new NotificationsDatabase();

        // Act
        var roles = database.Roles.Select(role => (role.Name, role.ConfigName)).ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(database.Name).IsEqualTo("notifications");
            await Assert.That(database.Schema).IsEqualTo("notifications");
            await Assert.That(roles).IsEquivalentTo(
                [("frappe_notifications", "Notifications"), ("frappe_notifications_worker", "NotificationsWorker")]);
        }
    }

    [Test]
    public async Task The_Worker_grants_added_to_the_relays_are_the_ones_its_consumers_and_purge_need()
    {
        // Act
        var statements = NotificationsDatabase.WorkerStatements("notifications");

        // Assert
        await Assert.That(statements).IsEquivalentTo(
        [
            "GRANT INSERT ON TABLE notifications.inbox_messages TO frappe_notifications_worker",
            "GRANT INSERT, SELECT, DELETE ON TABLE notifications.deliveries TO frappe_notifications_worker",
            "GRANT SELECT ON TABLE notifications.suppressions TO frappe_notifications_worker",
            "GRANT SELECT ON TABLE notifications.preferences TO frappe_notifications_worker",
            "GRANT DELETE ON TABLE notifications.provider_events TO frappe_notifications_worker",
            "GRANT SELECT (received_at) ON TABLE notifications.provider_events TO frappe_notifications_worker",
            "GRANT SELECT (provider, provider_message_id, feedback, occurred_at) ON TABLE notifications.provider_events "
            + "TO frappe_notifications_worker",
        ]);
    }
}
