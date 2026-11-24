using Frappe.Notifications.Domain.Deliveries;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Domain;

/// <summary>A delivery follows the provider's feedback through guarded transitions; a terminal status never regresses (S14).</summary>
public sealed class DeliveryTransitionTests
{
    private static readonly Instant Sent = Instant.FromUtc(2026, 11, 24, 10, 0);

    private static readonly Instant Later = Instant.FromUtc(2026, 11, 24, 10, 5);

    private static readonly DeliveryTarget Target = new(
        Guid.Parse("0192b6f0-0000-7000-8000-000000000001"), "test_security", NotificationCategory.Security, ChannelName.Create("mail").Value,
        RecipientKey.Of(new ReferenceRecipient("account", "42")), null);

    public static IEnumerable<(DeliveryStatus Start, DeliveryFeedback Feedback, DeliveryStatus End)> Allowed()
    {
        yield return (DeliveryStatus.Sent, DeliveryFeedback.Delivered, DeliveryStatus.Delivered);
        yield return (DeliveryStatus.Sent, DeliveryFeedback.PermanentFailure, DeliveryStatus.Bounced);
        yield return (DeliveryStatus.Sent, DeliveryFeedback.Complaint, DeliveryStatus.Complained);
        yield return (DeliveryStatus.Sent, DeliveryFeedback.Delayed, DeliveryStatus.Delayed);
        yield return (DeliveryStatus.Delayed, DeliveryFeedback.Delivered, DeliveryStatus.Delivered);
        yield return (DeliveryStatus.Delayed, DeliveryFeedback.PermanentFailure, DeliveryStatus.Bounced);
        yield return (DeliveryStatus.Delivered, DeliveryFeedback.Complaint, DeliveryStatus.Complained);
    }

    public static IEnumerable<(DeliveryStatus Start, DeliveryFeedback Feedback)> Refused()
    {
        yield return (DeliveryStatus.Delayed, DeliveryFeedback.Complaint);
        yield return (DeliveryStatus.Delivered, DeliveryFeedback.PermanentFailure);
        yield return (DeliveryStatus.Delivered, DeliveryFeedback.Delayed);
        yield return (DeliveryStatus.Bounced, DeliveryFeedback.Delivered);
        yield return (DeliveryStatus.Bounced, DeliveryFeedback.Complaint);
        yield return (DeliveryStatus.Complained, DeliveryFeedback.Delivered);
        yield return (DeliveryStatus.Complained, DeliveryFeedback.PermanentFailure);
        yield return (DeliveryStatus.Failed, DeliveryFeedback.Delivered);
        yield return (DeliveryStatus.Unresolved, DeliveryFeedback.Delivered);
    }

    private static Delivery InStatus(DeliveryStatus status)
    {
        var delivery = status switch
        {
            DeliveryStatus.Failed => Delivery.Failed(Guid.NewGuid(), Target, DeliveryFailureReason.ProviderRejected, Sent),
            DeliveryStatus.Unresolved => Delivery.Unresolved(Guid.NewGuid(), Target, Sent),
            _ => Delivery.Sent(Guid.NewGuid(), Target, DeliveryProvider.Resend, "msg_1", null, Sent),
        };
        var path = status switch
        {
            DeliveryStatus.Delayed => new[] { DeliveryFeedback.Delayed },
            DeliveryStatus.Delivered => [DeliveryFeedback.Delivered],
            DeliveryStatus.Bounced => [DeliveryFeedback.PermanentFailure],
            DeliveryStatus.Complained => [DeliveryFeedback.Complaint],
            _ => [],
        };
        foreach (var feedback in path)
        {
            delivery.Apply(feedback, Sent);
        }

        return delivery;
    }

    [Test]
    [MethodDataSource(nameof(Allowed))]
    public async Task An_allowed_feedback_moves_the_delivery_and_stamps_the_change(
        DeliveryStatus start, DeliveryFeedback feedback, DeliveryStatus end)
    {
        // Arrange
        var delivery = InStatus(start);

        // Act
        var result = delivery.Apply(feedback, Later);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(delivery.Status).IsEqualTo(end);
            await Assert.That(delivery.StatusChangedAt).IsEqualTo(Later);
        }
    }

    [Test]
    [MethodDataSource(nameof(Refused))]
    public async Task A_feedback_that_would_regress_a_status_is_refused_and_changes_nothing(DeliveryStatus start, DeliveryFeedback feedback)
    {
        // Arrange
        var delivery = InStatus(start);

        // Act
        var result = delivery.Apply(feedback, Later);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.delivery.transition_not_allowed");
            await Assert.That(delivery.Status).IsEqualTo(start);
            await Assert.That(delivery.StatusChangedAt).IsEqualTo(Sent);
        }
    }

    [Test]
    public async Task A_delivered_feedback_records_when_it_was_delivered()
    {
        // Arrange
        var delivery = InStatus(DeliveryStatus.Sent);

        // Act
        delivery.Apply(DeliveryFeedback.Delivered, Later);

        // Assert
        await Assert.That(delivery.DeliveredAt).IsEqualTo(Later);
    }

    [Test]
    public async Task The_first_delivered_feedback_raises_the_time_from_sent_to_delivered_once_and_a_delay_first_changes_nothing()
    {
        // Arrange
        var delivery = InStatus(DeliveryStatus.Sent);
        delivery.Apply(DeliveryFeedback.Delayed, Later.Plus(Duration.FromMinutes(1)));

        // Act
        delivery.Apply(DeliveryFeedback.Delivered, Later);
        delivery.Apply(DeliveryFeedback.Delivered, Later.Plus(Duration.FromMinutes(1)));

        // Assert
        var raised = delivery.DomainEvents.OfType<DeliveryDelivered>().Single();
        using (Assert.Multiple())
        {
            await Assert.That(raised.Kind).IsEqualTo("test_security");
            await Assert.That(raised.Channel).IsEqualTo("mail");
            await Assert.That(raised.TimeToDelivered).IsEqualTo(TimeSpan.FromMinutes(5));
        }
    }

    [Test]
    public async Task A_delivery_the_provider_reports_before_it_was_sent_has_a_zero_time_to_delivered()
    {
        // Arrange
        var delivery = InStatus(DeliveryStatus.Sent);

        // Act
        delivery.Apply(DeliveryFeedback.Delivered, Sent.Minus(Duration.FromSeconds(2)));

        // Assert
        await Assert.That(delivery.DomainEvents.OfType<DeliveryDelivered>().Single().TimeToDelivered).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task Repeating_the_current_status_and_a_provider_suppression_change_nothing_and_succeed()
    {
        // Arrange
        var delivery = InStatus(DeliveryStatus.Delivered);

        // Act
        var repeated = delivery.Apply(DeliveryFeedback.Delivered, Later);
        var suppressed = delivery.Apply(DeliveryFeedback.SuppressedByProvider, Later);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(repeated.IsSuccess).IsTrue();
            await Assert.That(suppressed.IsSuccess).IsTrue();
            await Assert.That(delivery.Status).IsEqualTo(DeliveryStatus.Delivered);
            await Assert.That(delivery.StatusChangedAt).IsEqualTo(Sent);
        }
    }

    [Test]
    public async Task A_sent_delivery_holds_the_provider_pair_the_target_and_no_address_in_the_clear()
    {
        // Arrange
        var hash = new AddressHash(new byte[32], 3);
        var target = Target with { RecipientKey = RecipientKey.Of(hash), AddressHash = hash };

        // Act
        var delivery = Delivery.Sent(Guid.NewGuid(), target, DeliveryProvider.Resend, "msg_9", null, Sent);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(delivery.Provider).IsEqualTo(DeliveryProvider.Resend);
            await Assert.That(delivery.ProviderMessageId).IsEqualTo("msg_9");
            await Assert.That(delivery.SentAt).IsEqualTo(Sent);
            await Assert.That(delivery.HashKeyId).IsEqualTo((short)3);
            await Assert.That(delivery.AddressHash).IsEquivalentTo(new byte[32]);
            await Assert.That(delivery.RecipientKey).IsEqualTo($"addr:3:{new string('0', 64)}");
        }
    }
}
