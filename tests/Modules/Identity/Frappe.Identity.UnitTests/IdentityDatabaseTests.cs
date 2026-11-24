using Frappe.Identity.Infrastructure.Persistence;

namespace Frappe.Identity.UnitTests;

/// <summary>What the Migrator is told about Identity: its schema and its two login roles, without a database.</summary>
public sealed class IdentityDatabaseTests
{
    [Test]
    public async Task It_owns_the_identity_schema_and_names_the_two_login_roles_with_their_password_keys()
    {
        // Arrange
        var database = new IdentityDatabase();

        // Act
        var roles = database.Roles.Select(role => (role.Name, role.ConfigName)).ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(database.Name).IsEqualTo("identity");
            await Assert.That(database.Schema).IsEqualTo("identity");
            await Assert.That(roles).IsEquivalentTo([("frappe_identity", "Identity"), ("frappe_identity_worker", "IdentityWorker")]);
        }
    }

    [Test]
    public async Task The_Worker_grants_added_to_the_relays_are_the_outbox_insert_and_the_columns_of_the_sweep_and_the_purge()
    {
        // Act
        var statements = IdentityDatabase.WorkerStatements("identity");

        // Assert
        await Assert.That(statements).IsEquivalentTo(
        [
            "GRANT INSERT ON TABLE identity.outbox_messages TO frappe_identity_worker",
            "GRANT SELECT (id, principal_id, idle_expires_at, absolute_expires_at, ended_at, revoked_at) "
            + "ON TABLE identity.sessions TO frappe_identity_worker",
            "GRANT UPDATE (ended_at) ON TABLE identity.sessions TO frappe_identity_worker",
            "GRANT DELETE ON TABLE identity.sessions TO frappe_identity_worker",
            "GRANT SELECT (id, expires_at) ON TABLE identity.one_time_codes TO frappe_identity_worker",
            "GRANT DELETE ON TABLE identity.one_time_codes TO frappe_identity_worker",
            "GRANT SELECT (id, expires_at, redeemed_at) ON TABLE identity.email_change_undos TO frappe_identity_worker",
            "GRANT DELETE ON TABLE identity.email_change_undos TO frappe_identity_worker",
            "GRANT SELECT (principal_id, registered_at, verified_at) ON TABLE identity.owners TO frappe_identity_worker",
            "GRANT SELECT (id, status) ON TABLE identity.principals TO frappe_identity_worker",
            "GRANT DELETE ON TABLE identity.principals TO frappe_identity_worker",
        ]);
    }
}
