using System.Diagnostics;
using Frappe.Notifications.Application.Deliveries;
using Frappe.Notifications.Application.Senders;
using Frappe.Notifications.Domain.Deliveries;
using Microsoft.Extensions.Logging;

namespace Frappe.Notifications.UnitTests.Deliveries;

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
        world.Pending.CountCommitted();

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
    public async Task Nothing_is_counted_until_the_claim_committed_and_a_second_commit_counts_nothing_again()
    {
        // Arrange
        using var world = new DispatchWorld();
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);
        var before = world.Capture.Of("frappe.notifications.deliveries").Count;

        // Act
        world.Pending.CountCommitted();
        var afterCommit = world.Capture.Of("frappe.notifications.deliveries").Count;
        world.Pending.CountCommitted();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before).IsEqualTo(0);
            await Assert.That(afterCommit).IsEqualTo(1);
            await Assert.That(world.Capture.Of("frappe.notifications.deliveries").Count).IsEqualTo(1);
            await Assert.That(world.Pending.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_dispatch_that_throws_leaves_nothing_counted()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Mail.Fault = new HttpRequestException("429");

        // Act
        await Assert.ThrowsAsync<HttpRequestException>(() => world.DispatchAsync(DispatchWorld.Security, ToAddress));
        world.Pending.CountCommitted();

        // Assert
        await Assert.That(world.Capture.Of("frappe.notifications.deliveries")).IsEmpty();
    }

    [Test]
    public async Task An_unresolved_reference_of_a_kind_no_channel_carries_is_counted_once_as_no_channel_and_stages_nothing()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Mail.Accepts = false;

        // Act
        var outcomes = await world.DispatchAsync(DispatchWorld.Security, ToReference);
        world.Pending.CountCommitted();

        // Assert
        var counted = world.Capture.Of("frappe.notifications.deliveries").Single();
        using (Assert.Multiple())
        {
            await Assert.That(outcomes).IsEquivalentTo([DispatchOutcome.NoChannel]);
            await Assert.That(world.Staging.Staged).IsEmpty();
            await Assert.That((string?)counted.Tags["frappe.outcome"]).IsEqualTo("no_channel");
            await Assert.That((string?)counted.Tags["kind"]).IsEqualTo("test_security");
            await Assert.That((string?)counted.Tags["channel"]).IsEqualTo("none");
        }
    }

    [Test]
    public async Task A_failed_an_unresolved_and_a_suppressed_outcome_are_logged_with_kind_and_channel_and_no_address()
    {
        // Arrange
        using var world = new DispatchWorld();
        world.Suppressions.Suppressed.Add(world.Hasher.Hash("blocked@example.com"));
        world.Mail.Outcome = new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected);

        // Act
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);
        await world.DispatchAsync(DispatchWorld.Security, new TestPayload("blocked@example.com", null));
        await world.DispatchAsync(DispatchWorld.Security, ToReference);

        // Assert
        var entries = world.Logs.Entries.Where(e => e.Category.Contains("NotificationDispatcher", StringComparison.Ordinal)).ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(entries.Select(e => e.Level)).IsEquivalentTo([LogLevel.Warning, LogLevel.Information, LogLevel.Information]);
            await Assert.That(entries.Select(e => e.Message)).IsEquivalentTo(
            [
                "Notification test_security over mail failed for good and is recorded as failed",
                "Notification test_security over mail was skipped: the address is suppressed",
                "Notification test_security over mail has a recipient nobody resolved and is recorded as unresolved",
            ]);
            await Assert.That(string.Join(' ', entries.Select(e => e.Message))).DoesNotContain("example.com");
        }
    }

    [Test]
    public async Task Each_channel_delivery_runs_in_one_span_tagged_with_kind_channel_and_outcome()
    {
        // Arrange
        using var world = new DispatchWorld();
        var spans = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Frappe.Notifications" or "Frappe.Notifications.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        // Act: the tests run in parallel, so the span is told apart by the trace of this test's own parent.
        using var testSource = new ActivitySource("Frappe.Notifications.Tests");
        using var parent = testSource.StartActivity("test");
        await world.DispatchAsync(DispatchWorld.Security, ToAddress);

        // Assert
        var span = spans.Single(s => s.TraceId == parent!.TraceId && s.Source.Name == "Frappe.Notifications");
        using (Assert.Multiple())
        {
            await Assert.That(span.GetTagItem("kind")).IsEqualTo("test_security");
            await Assert.That(span.GetTagItem("channel")).IsEqualTo("mail");
            await Assert.That(span.GetTagItem("frappe.outcome")).IsEqualTo("sent");
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
        var request = new DeliveryRequest(DispatchWorld.MessageId, DispatchWorld.Security, ToAddress).ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(item).DoesNotContain("example.com");
            await Assert.That(request).DoesNotContain("example.com");
        }
    }
}
