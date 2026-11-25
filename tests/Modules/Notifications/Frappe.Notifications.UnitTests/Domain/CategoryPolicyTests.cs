using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Domain.Suppressions;
using NodaTime;

namespace Frappe.Notifications.UnitTests.Domain;

/// <summary>What each category permits, and the preference and suppression that enforce it (S9).</summary>
public sealed class CategoryPolicyTests
{
    [Test]
    [Arguments(NotificationCategory.Security, false, false)]
    [Arguments(NotificationCategory.Transactional, false, true)]
    [Arguments(NotificationCategory.Marketing, true, true)]
    public async Task Only_marketing_can_be_turned_off_and_only_security_carries_no_links(
        NotificationCategory category, bool canOptOut, bool allowsLinks)
    {
        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(category.CanOptOut).IsEqualTo(canOptOut);
            await Assert.That(category.AllowsLinks).IsEqualTo(allowsLinks);
        }
    }

    [Test]
    public async Task The_email_changed_notice_is_transactional_so_it_carries_the_undo_link_and_cannot_be_turned_off()
    {
        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Frappe.Notifications.Messages.Account.EmailChanged.Category).IsEqualTo(NotificationCategory.Transactional);
            await Assert.That(Frappe.Notifications.Messages.Account.EmailChanged.Category.CanOptOut).IsFalse();
        }
    }

    [Test]
    public async Task A_recipient_can_opt_out_of_marketing()
    {
        // Arrange
        var key = RecipientKey.Of(new ReferenceRecipient("account", "42"));
        var now = Instant.FromUtc(2026, 11, 24, 10, 0);

        // Act
        var result = Preference.OptOut(key, NotificationCategory.Marketing, now);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.RecipientKey).IsEqualTo("ref:account:42");
            await Assert.That(result.Value.Category).IsEqualTo(NotificationCategory.Marketing);
            await Assert.That(result.Value.OptedOutAt).IsEqualTo(now);
        }
    }

    [Test]
    [Arguments(NotificationCategory.Security)]
    [Arguments(NotificationCategory.Transactional)]
    public async Task A_category_that_cannot_be_turned_off_refuses_an_opt_out(NotificationCategory category)
    {
        // Act
        var result = Preference.OptOut(RecipientKey.Of(new ReferenceRecipient("account", "42")), category, Instant.FromUtc(2026, 11, 24, 10, 0));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.preference.category_cannot_opt_out");
        }
    }

    [Test]
    public async Task A_suppression_keeps_the_channel_the_hash_and_what_the_provider_said()
    {
        // Arrange
        var hash = new AddressHash(new byte[32], 2);
        var now = Instant.FromUtc(2026, 11, 24, 10, 0);

        // Act
        var suppression = Suppression.Add(
            ChannelName.Create("mail").Value, hash, SuppressionReason.Complaint, SuppressionSource.Webhook, now, DeliveryProvider.Resend, "evt_1");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(suppression.Channel).IsEqualTo("mail");
            await Assert.That(suppression.AddressHash).IsEquivalentTo(new byte[32]);
            await Assert.That(suppression.HashKeyId).IsEqualTo((short)2);
            await Assert.That(suppression.Reason).IsEqualTo(SuppressionReason.Complaint);
            await Assert.That(suppression.Provider).IsEqualTo(DeliveryProvider.Resend);
            await Assert.That(suppression.ProviderEventId).IsEqualTo("evt_1");
            await Assert.That(suppression.CreatedAt).IsEqualTo(now);
        }
    }
}
