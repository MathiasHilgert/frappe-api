using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>The provider idempotency key is a pure function of message, kind, recipient and channel (S14).</summary>
public sealed class DeliveryIdempotencyTests
{
    private static readonly Guid Message = Guid.Parse("0192b6f0-0000-7000-8000-000000000001");

    private static readonly RecipientKey Recipient = RecipientKey.Of(new ReferenceRecipient("account", "42"));

    [Test]
    public async Task The_same_inputs_always_give_the_same_key_that_reveals_no_recipient()
    {
        // Act
        var first = DeliveryIdempotency.Key(Message, "account_activation_code", Recipient, TestChannels.Mail);
        var second = DeliveryIdempotency.Key(Message, "account_activation_code", Recipient, TestChannels.Mail);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(second);
            await Assert.That(first.Length).IsEqualTo(64);
            await Assert.That(first).DoesNotContain("42");
        }
    }

    [Test]
    public async Task Changing_any_one_part_changes_the_key()
    {
        // Arrange
        var baseline = DeliveryIdempotency.Key(Message, "kind_a", Recipient, TestChannels.Mail);

        // Act
        var keys = new[]
        {
            DeliveryIdempotency.Key(Guid.Parse("0192b6f0-0000-7000-8000-000000000002"), "kind_a", Recipient, TestChannels.Mail),
            DeliveryIdempotency.Key(Message, "kind_b", Recipient, TestChannels.Mail),
            DeliveryIdempotency.Key(Message, "kind_a", RecipientKey.Of(new ReferenceRecipient("account", "43")), TestChannels.Mail),
            DeliveryIdempotency.Key(Message, "kind_a", Recipient, TestChannels.Text),
        };

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(keys.Distinct().Count()).IsEqualTo(4);
            await Assert.That(keys.Contains(baseline)).IsFalse();
        }
    }
}
