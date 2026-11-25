using Frappe.Notifications.Application.ProviderEvents;
using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>
/// What the provider reported before a delivery was recorded is applied when it is, oldest first, through the delivery's own guards (Flow 3).
/// </summary>
public sealed class ParkedFeedbackDispatchTests
{
    private static readonly TestPayload ToAddress = new("ana@example.com", null);

    private static ParkedFeedback At(DeliveryFeedback feedback, int minute) => new(feedback, Instant.FromUtc(2026, 11, 24, 10, minute));

    [Test]
    public async Task A_report_parked_for_the_message_is_applied_to_the_delivery_as_it_is_staged()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Staging.Parked["msg_1"] = [At(DeliveryFeedback.Delivered, 1)];

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        var staged = world.Staging.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(staged.Status).IsEqualTo(DeliveryStatus.Delivered);
            await Assert.That(staged.DeliveredAt).IsEqualTo(Instant.FromUtc(2026, 11, 24, 10, 1));
        }
    }

    [Test]
    public async Task Parked_reports_are_applied_in_the_order_given_and_a_refused_move_changes_nothing()
    {
        // Arrange: the port returns them oldest first; a delay after the delivery is refused, the complaint is applied.
        using var world = new DispatchWorld();
        world.Staging.Parked["msg_1"] = [At(DeliveryFeedback.Delivered, 1), At(DeliveryFeedback.Delayed, 2), At(DeliveryFeedback.Complaint, 3)];

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        await Assert.That(world.Staging.Staged.Single().Status).IsEqualTo(DeliveryStatus.Complained);
    }

    [Test]
    public async Task A_report_parked_for_another_message_is_not_applied()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Staging.Parked["msg_other"] = [At(DeliveryFeedback.Delivered, 1)];

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        await Assert.That(world.Staging.Staged.Single().Status).IsEqualTo(DeliveryStatus.Sent);
    }
}
