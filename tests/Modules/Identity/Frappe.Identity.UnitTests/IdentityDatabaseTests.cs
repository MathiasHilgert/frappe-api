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
    public async Task The_Worker_grant_added_to_the_relays_is_the_outbox_insert_alone()
    {
        // Act
        var statements = IdentityDatabase.WorkerStatements("identity");

        // Assert
        await Assert.That(statements).IsEquivalentTo(["GRANT INSERT ON TABLE identity.outbox_messages TO frappe_identity_worker"]);
    }
}
