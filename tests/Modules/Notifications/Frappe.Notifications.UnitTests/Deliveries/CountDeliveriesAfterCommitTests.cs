using Frappe.Notifications.Contracts;
using Frappe.Notifications.Infrastructure.Messaging;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>The consumer's after-commit step counts what the dispatch decided, once the claim committed (OBS-06).</summary>
public sealed class CountDeliveriesAfterCommitTests
{
    [Test]
    public async Task The_step_counts_the_pending_deliveries_and_a_second_run_of_it_counts_nothing()
    {
        // Arrange
        using var world = new DispatchWorld();
        await world.DispatchAsync(DispatchWorld.Security, new TestPayload("ana@example.com", null));
        world.Pending.MarkClaimed();
        var step = new CountDeliveriesAfterCommit<NotificationRequested>(world.Pending);
        var message = new NotificationRequested(Guid.CreateVersion7(), "test_security", default);

        // Act
        await step.HandleAsync(message, CancellationToken.None);
        await step.HandleAsync(message, CancellationToken.None);

        // Assert
        var counted = world.Capture.Of("frappe.notifications.deliveries").Select(m => (string?)m.Tags["frappe.outcome"]);
        await Assert.That(counted).IsEquivalentTo(new string?[] { "sent" });
    }

    [Test]
    public async Task A_claim_lost_to_a_racing_delivery_counts_nothing()
    {
        // Arrange
        using var world = new DispatchWorld();
        await world.DispatchAsync(DispatchWorld.Security, new TestPayload("ana@example.com", null));
        var step = new CountDeliveriesAfterCommit<NotificationRequested>(world.Pending);

        // Act
        await step.HandleAsync(new NotificationRequested(Guid.CreateVersion7(), "test_security", default), CancellationToken.None);

        // Assert
        await Assert.That(world.Capture.Of("frappe.notifications.deliveries")).IsEmpty();
        await Assert.That(world.Pending.Count).IsEqualTo(0);
    }
}
