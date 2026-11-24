using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Dispatching;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>The dispatch pipeline end to end over fakes: resolve, preference gate, channels, suppression gate, deliver, stage.</summary>
public sealed class NotificationDispatcherTests
{
    private static readonly TestPayload ToAddress = new("Ana@Example.com", null);

    private static readonly TestPayload ToReference = new(null, "42");

    [Test]
    public async Task An_explicit_contact_is_delivered_over_its_channel_and_the_sent_delivery_is_staged_unsaved()
    {
        // Arrange
        using var world = new DispatchWorld();

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        var staged = world.Staging.Staged.Single();
        var hash = world.Hasher.Hash("ana@example.com");
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Sent]);
            await Assert.That(world.Mail.Delivered.Single().Contact.Address).IsEqualTo("Ana@Example.com");
            await Assert.That(staged.Status).IsEqualTo(DeliveryStatus.Sent);
            await Assert.That(staged.Kind).IsEqualTo("test_security");
            await Assert.That(staged.Category).IsEqualTo(NotificationCategory.Security);
            await Assert.That(staged.Channel).IsEqualTo("mail");
            await Assert.That(staged.RecipientKey).IsEqualTo(RecipientKey.Of(hash).Value);
            await Assert.That(staged.AddressHash).IsEquivalentTo(hash.Value);
            await Assert.That(staged.ProviderMessageId).IsEqualTo("msg_1");
            await Assert.That(staged.SourceMessageId).IsEqualTo(DispatchWorld.MessageId);
            await Assert.That(staged.LocaleServed!.Value).IsEqualTo("es-AR");
        }
    }

    [Test]
    public async Task The_channel_gets_the_idempotency_key_derived_from_message_kind_recipient_and_channel()
    {
        // Arrange
        using var world = new DispatchWorld();
        var recipient = RecipientKey.Of(world.Hasher.Hash(ToAddress.Address!));

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        await Assert.That(world.Mail.Delivered.Single().IdempotencyKey)
            .IsEqualTo(DeliveryIdempotency.Key(DispatchWorld.MessageId, "test_security", recipient, TestChannels.Mail));
    }

    [Test]
    public async Task A_reference_is_resolved_by_the_directory_and_keeps_its_reference_key()
    {
        // Arrange
        using var world = new DispatchWorld(
            new FakeResolver("account", new() { ["42"] = [DispatchWorld.Contact(TestChannels.Mail, "ana@example.com")] }));

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        var staged = world.Staging.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Sent]);
            await Assert.That(staged.RecipientKey).IsEqualTo("ref:account:42");
            await Assert.That(staged.AddressHash).IsEquivalentTo(world.Hasher.Hash("ana@example.com").Value);
            await Assert.That(world.Mail.Delivered.Single().Contact.Address).IsEqualTo("ana@example.com");
        }
    }

    [Test]
    public async Task A_reference_no_directory_resolves_ends_unresolved_without_sending_anything()
    {
        // Arrange
        using var world = new DispatchWorld();

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        var staged = world.Staging.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Unresolved]);
            await Assert.That(staged.Status).IsEqualTo(DeliveryStatus.Unresolved);
            await Assert.That(staged.RecipientKey).IsEqualTo("ref:account:42");
            await Assert.That(staged.Channel).IsEqualTo("mail");
            await Assert.That(world.Mail.Delivered).IsEmpty();
        }
    }

    [Test]
    public async Task A_reference_whose_resolver_knows_nobody_by_that_id_is_unresolved()
    {
        // Arrange
        using var world = new DispatchWorld(new FakeResolver("account", []));

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Unresolved]);
    }

    [Test]
    public async Task An_unresolved_delivery_already_recorded_is_not_staged_again()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Staging.AlreadyRecorded = true;

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Duplicate]);
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_marketing_notification_the_recipient_turned_off_is_neither_sent_nor_recorded()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Preferences.OptedOut.Add(RecipientKey.Of(world.Hasher.Hash(ToAddress.Address!)).Value);

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Marketing, ToAddress);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.OptedOut]);
            await Assert.That(world.Mail.Delivered).IsEmpty();
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_security_notification_ignores_a_stored_opt_out_because_it_cannot_be_turned_off()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Preferences.OptedOut.Add(RecipientKey.Of(world.Hasher.Hash(ToAddress.Address!)).Value);

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Sent]);
    }

    [Test]
    public async Task A_suppressed_address_is_not_contacted_even_when_it_was_suppressed_under_an_older_key()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Suppressions.Suppressed.Add(world.Hasher.HashWithEveryKey(ToAddress.Address!).Single());

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Suppressed]);
            await Assert.That(world.Mail.Delivered).IsEmpty();
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_delivery_already_recorded_is_not_sent_again()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Staging.AlreadyRecorded = true;

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Duplicate]);
            await Assert.That(world.Mail.Delivered).IsEmpty();
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_kind_no_channel_can_carry_to_the_contact_is_not_sent()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Mail.Accepts = false;

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.NoChannel]);
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task A_contact_on_a_channel_nobody_registered_is_not_sent_over_another_channel()
    {
        // Arrange
        using var world = new DispatchWorld(
            new FakeResolver("account", new() { ["42"] = [DispatchWorld.Contact(TestChannels.Text, "+5491100000000")] }));
        world.Text.Accepts = false;

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.NoChannel]);
            await Assert.That(world.Mail.Delivered).IsEmpty();
        }
    }

    [Test]
    public async Task A_permanent_provider_refusal_stages_a_failed_delivery_with_its_reason()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Mail.Outcome = new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected);

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        var staged = world.Staging.Staged.Single();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.Failed]);
            await Assert.That(staged.Status).IsEqualTo(DeliveryStatus.Failed);
            await Assert.That(staged.FailureReason).IsEqualTo(DeliveryFailureReason.ProviderRejected);
        }
    }

    [Test]
    public async Task A_transient_provider_fault_propagates_so_the_message_is_redelivered_and_nothing_is_staged()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Mail.Fault = new HttpRequestException("429");

        // Act
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => world.DispatchAsync(DispatchWorld.Security, ToAddress));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(thrown).IsNotNull();
            await Assert.That(world.Staging.Staged).IsEmpty();
        }
    }

    [Test]
    public async Task Each_intended_delivery_is_counted_by_kind_channel_and_outcome_without_any_address()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Suppressions.Suppressed.Add(world.Hasher.Hash("blocked@example.com"));

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);
        await world.DispatchAsync(DispatchWorld.Security, new TestPayload("blocked@example.com", null));

        // Assert
        var counted = world.Capture.Of("frappe.notifications.deliveries");
        using (Assert.Multiple())
        {
            await Assert.That(counted.Count).IsEqualTo(2);
            await Assert.That(counted.Select(m => (string?)m.Tags["frappe.outcome"])).IsEquivalentTo(new string?[] { "sent", "suppressed" });
            await Assert.That(counted.All(m => (string?)m.Tags["kind"] == "test_security" && (string?)m.Tags["channel"] == "mail")).IsTrue();
            await Assert.That(counted.All(m => m.Tags.Count == 3)).IsTrue();
        }
    }

    [Test]
    public async Task A_dispatch_item_and_a_request_never_print_their_payload_or_address()
    {
        // Arrange
        using var world = new DispatchWorld();
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Act
        var item = world.Mail.Delivered.Single().ToString();
        var request = new NotificationRequest(DispatchWorld.MessageId, DispatchWorld.Security, ToAddress).ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(item).DoesNotContain("example.com");
            await Assert.That(request).DoesNotContain("example.com");
        }
    }
}
