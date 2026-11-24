using Frappe.Notifications.Application.Feedback;
using Frappe.Notifications.Application.Requests.RecordProviderFeedback;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Notifications.UnitTests.Dispatching;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Feedback;

/// <summary>
/// What a provider's report does (S4, Flow 3): it dedupes by event id, moves the delivery through its guarded transitions, parks what arrives
/// before its delivery, and suppresses the address of a complaint, a hard bounce or a provider suppression and of nothing else.
/// </summary>
public sealed class RecordProviderFeedbackTests
{
    private const string MessageId = "re_1";

    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 10, 0);

    private static readonly Instant Before = Instant.FromUtc(2026, 11, 24, 9, 59);

    private static readonly ChannelName Email = ChannelName.Create("email").Value;

    /// <summary>The fakes a test arranges and the handler over them.</summary>
    private sealed class World
    {
        public FakeProviderEvents Events { get; } = new();

        public FakeDeliveries Deliveries { get; } = new();

        public FakeSuppressions Suppressions { get; } = new();

        public Delivery Delivery { get; } = Delivery.Sent(
            Guid.NewGuid(),
            new DeliveryTarget(
                Guid.NewGuid(), "account_activation_code", NotificationCategory.Security, Email,
                RecipientKey.Of(new ReferenceRecipient("account", "42")), null),
            DeliveryProvider.Resend, MessageId, null, Before);

        public World() => Deliveries.ByMessage[MessageId] = Delivery;

        public Task<Frappe.Results.Result<Frappe.Results.Unit>> RecordAsync(
            DeliveryFeedback feedback, string eventId = "evt_1", string? messageId = MessageId, params string[] addresses)
        {
            var handler = new RecordProviderFeedbackHandler(Events, Deliveries, Suppressions, TestHasher.Create(), new Clock());
            var provider = new ProviderFeedback(
                DeliveryProvider.Resend, eventId, messageId, feedback, Now, Email, addresses.Length == 0 ? ["ana@example.com"] : addresses);
            return handler.HandleAsync(new RecordProviderFeedback(provider), CancellationToken.None);
        }
    }

    /// <summary>A clock stopped at <see cref="Now"/>.</summary>
    private sealed class Clock : IClock
    {
        public Instant GetCurrentInstant() => Now;
    }

    [Test]
    public async Task A_delivered_report_moves_the_delivery_records_the_event_as_matched_and_suppresses_nothing()
    {
        // Arrange
        var world = new World();

        // Act
        var result = await world.RecordAsync(DeliveryFeedback.Delivered);

        // Assert
        var recorded = world.Events.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(world.Delivery.Status).IsEqualTo(DeliveryStatus.Delivered);
            await Assert.That(world.Delivery.DeliveredAt).IsEqualTo(Now);
            await Assert.That(recorded.EventId).IsEqualTo("evt_1");
            await Assert.That(recorded.Matched).IsTrue();
            await Assert.That(recorded.ProviderMessageId).IsNull();
            await Assert.That(recorded.DomainEvents.Single()).IsEqualTo(new ProviderEventRecorded(DeliveryFeedback.Delivered, Matched: true));
            await Assert.That(world.Suppressions.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task An_event_already_recorded_is_ignored_whatever_it_says()
    {
        // Arrange
        var world = new World();
        world.Events.Known.Add("evt_1");

        // Act
        var result = await world.RecordAsync(DeliveryFeedback.Complaint);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(world.Delivery.Status).IsEqualTo(DeliveryStatus.Sent);
            await Assert.That(world.Events.Staged).IsEmpty();
            await Assert.That(world.Suppressions.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_delay_after_the_delivery_is_ignored_but_the_event_is_still_recorded()
    {
        // Arrange
        var world = new World();
        await world.RecordAsync(DeliveryFeedback.Delivered, "evt_1");

        // Act
        var result = await world.RecordAsync(DeliveryFeedback.Delayed, "evt_2");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(world.Delivery.Status).IsEqualTo(DeliveryStatus.Delivered);
            await Assert.That(world.Events.Staged.Select(e => e.EventId)).IsEquivalentTo(["evt_1", "evt_2"]);
        }
    }

    [Test]
    public async Task A_complaint_after_the_delivery_is_applied_and_suppresses_the_address()
    {
        // Arrange
        var world = new World();
        await world.RecordAsync(DeliveryFeedback.Delivered, "evt_1");

        // Act
        await world.RecordAsync(DeliveryFeedback.Complaint, "evt_2");

        // Assert
        var suppression = world.Suppressions.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(world.Delivery.Status).IsEqualTo(DeliveryStatus.Complained);
            await Assert.That(suppression.Reason).IsEqualTo(SuppressionReason.Complaint);
            await Assert.That(suppression.Source).IsEqualTo(SuppressionSource.Webhook);
            await Assert.That(suppression.Channel).IsEqualTo("email");
            await Assert.That(suppression.Provider).IsEqualTo(DeliveryProvider.Resend);
            await Assert.That(suppression.ProviderEventId).IsEqualTo("evt_2");
            await Assert.That(suppression.AddressHash).IsEquivalentTo(TestHasher.Create().Hash("ana@example.com").Value);
            await Assert.That(suppression.DomainEvents.Single()).IsEqualTo(new SuppressionAdded(SuppressionReason.Complaint));
        }
    }

    [Test]
    [Arguments(DeliveryFeedback.PermanentFailure, SuppressionReason.HardBounce)]
    [Arguments(DeliveryFeedback.SuppressedByProvider, SuppressionReason.ProviderSuppression)]
    public async Task A_hard_bounce_and_a_provider_suppression_suppress_the_address_even_when_the_delivery_refuses_the_move(
        DeliveryFeedback feedback, SuppressionReason reason)
    {
        // Arrange: a delivery already delivered refuses to become bounced, but the address is still no good.
        var world = new World();
        await world.RecordAsync(DeliveryFeedback.Delivered, "evt_1");

        // Act
        await world.RecordAsync(feedback, "evt_2");

        // Assert
        await Assert.That(world.Suppressions.Staged.Single().Reason).IsEqualTo(reason);
    }

    [Test]
    public async Task A_delay_never_suppresses_and_an_address_already_suppressed_is_not_suppressed_twice()
    {
        // Arrange
        var world = new World();

        // Act
        await world.RecordAsync(DeliveryFeedback.Delayed, "evt_1");
        await world.RecordAsync(DeliveryFeedback.Complaint, "evt_2");
        await world.RecordAsync(DeliveryFeedback.PermanentFailure, "evt_3");

        // Assert
        await Assert.That(world.Suppressions.Staged.Select(s => s.Reason)).IsEquivalentTo([SuppressionReason.Complaint]);
    }

    [Test]
    public async Task An_event_before_its_delivery_is_parked_with_what_is_needed_to_apply_it_later_and_still_suppresses()
    {
        // Arrange
        var world = new World();
        world.Deliveries.ByMessage.Clear();

        // Act
        var result = await world.RecordAsync(DeliveryFeedback.Complaint);

        // Assert
        var parked = world.Events.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(parked.Matched).IsFalse();
            await Assert.That(parked.ProviderMessageId).IsEqualTo(MessageId);
            await Assert.That(parked.Feedback).IsEqualTo(DeliveryFeedback.Complaint);
            await Assert.That(parked.OccurredAt).IsEqualTo(Now);
            await Assert.That(parked.DomainEvents.Single()).IsEqualTo(new ProviderEventRecorded(DeliveryFeedback.Complaint, Matched: false));
            await Assert.That(world.Suppressions.Staged).HasSingleItem();
        }
    }

    [Test]
    public async Task An_event_that_names_no_message_is_recorded_unmatched_and_not_parked()
    {
        // Arrange
        var world = new World();

        // Act
        await world.RecordAsync(DeliveryFeedback.SuppressedByProvider, messageId: null);

        // Assert
        var recorded = world.Events.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(recorded.Matched).IsFalse();
            await Assert.That(recorded.ProviderMessageId).IsNull();
            await Assert.That(recorded.Feedback).IsNull();
            await Assert.That(world.Suppressions.Staged).HasSingleItem();
        }
    }
}
