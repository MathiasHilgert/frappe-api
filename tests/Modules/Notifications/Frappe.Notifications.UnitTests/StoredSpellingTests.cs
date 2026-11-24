using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Persistence;

namespace Frappe.Notifications.UnitTests;

/// <summary>
/// The text each closed set stores (DATA-06): a member's name in lower snake case is what a database holds and what its CHECK lists,
/// so renaming a member would silently change both. Each test pins one enum's spellings, in declaration order.
/// </summary>
public sealed class StoredSpellingTests
{
    [Test]
    public async Task NotificationCategory_is_stored_as_security_transactional_and_marketing()
    {
        // Act
        var list = StoredEnum<NotificationCategory>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'security', 'transactional', 'marketing'");
    }

    [Test]
    public async Task DeliveryStatus_is_stored_as_its_seven_statuses()
    {
        // Act
        var list = StoredEnum<DeliveryStatus>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'sent', 'delivered', 'delayed', 'bounced', 'complained', 'failed', 'unresolved'");
    }

    [Test]
    public async Task DeliveryFailureReason_is_stored_as_provider_rejected_and_invalid_recipient()
    {
        // Act
        var list = StoredEnum<DeliveryFailureReason>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'provider_rejected', 'invalid_recipient'");
    }

    [Test]
    public async Task DeliveryProvider_is_stored_as_resend()
    {
        // Act
        var list = StoredEnum<DeliveryProvider>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'resend'");
    }

    [Test]
    public async Task DeliveryFeedback_is_stored_as_the_five_channel_neutral_feedbacks()
    {
        // Act
        var list = StoredEnum<DeliveryFeedback>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'delivered', 'permanent_failure', 'complaint', 'delayed', 'suppressed_by_provider'");
    }

    [Test]
    public async Task SuppressionReason_is_stored_as_hard_bounce_complaint_and_provider_suppression()
    {
        // Act
        var list = StoredEnum<SuppressionReason>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'hard_bounce', 'complaint', 'provider_suppression'");
    }

    [Test]
    public async Task SuppressionSource_is_stored_as_webhook_and_manual()
    {
        // Act
        var list = StoredEnum<SuppressionSource>.CheckList;

        // Assert
        await Assert.That(list).IsEqualTo("'webhook', 'manual'");
    }
}
