using Frappe.Persistence;
using Frappe.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>The scheduler's job store is a database participant: the Migrator provisions its schema and role with no module present (R3).</summary>
public sealed class SchedulingDatabaseTests
{
    [Test]
    public async Task The_job_store_owns_the_quartz_schema_and_the_scheduler_role_with_data_access()
    {
        // Arrange
        await using var services = new ServiceCollection().AddFrappeScheduling("Host=db;Username=frappe_scheduler", _ => { }).BuildServiceProvider();

        // Act
        var participant = services.GetServices<IDatabaseParticipant>().Single();
        var role = participant.Roles.Single();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(participant.Schema).IsEqualTo("quartz");
            await Assert.That(participant.Name).IsEqualTo("scheduling");
            await Assert.That(role.Name).IsEqualTo("frappe_scheduler");
            await Assert.That(role.ConfigName).IsEqualTo("Scheduler");
            await Assert.That(role.Grant).IsSameReferenceAs(RoleGrant.Schema);
        }
    }
}
