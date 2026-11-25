using Frappe.Notifications.Senders.Email;

// S6418: the values are a published documentation vector and local-only placeholders; they protect nothing.
#pragma warning disable S6418

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// The webhook's signing secret is required wherever the webhook is served, and a host that lacks it fails its start naming the key
/// (S12, CFG-01).
/// </summary>
public sealed class ResendWebhookOptionsTests
{
    private static readonly ResendWebhookOptionsValidator Validator = new();

    [Test]
    public async Task A_missing_or_blank_secret_fails_naming_the_key()
    {
        // Act
        var missing = Validator.Validate(null, new ResendWebhookOptions());
        var blank = Validator.Validate(null, new ResendWebhookOptions { WebhookSecret = "  " });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(missing.Failed).IsTrue();
            await Assert.That(missing.FailureMessage).Contains("Notifications:Email:Resend:WebhookSecret");
            await Assert.That(blank.Failed).IsTrue();
        }
    }

    [Test]
    public async Task A_value_that_is_not_base64_or_is_too_short_fails_and_the_message_never_prints_it()
    {
        // Arrange
        const string notBase64 = "whsec_this is not base64 !!";
        const string tooShort = "whsec_AAAA";

        // Act
        var first = Validator.Validate(null, new ResendWebhookOptions { WebhookSecret = notBase64 });
        var second = Validator.Validate(null, new ResendWebhookOptions { WebhookSecret = tooShort });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Failed).IsTrue();
            await Assert.That(second.Failed).IsTrue();
            await Assert.That(first.FailureMessage).DoesNotContain("this is not");
            await Assert.That(second.FailureMessage).DoesNotContain("AAAA");
        }
    }

    [Test]
    public async Task A_secret_as_Resend_shows_it_passes_with_or_without_the_prefix()
    {
        // Act
        var withPrefix = Validator.Validate(null, new ResendWebhookOptions { WebhookSecret = WebhookSigning.Secret });
        var without = Validator.Validate(null, new ResendWebhookOptions { WebhookSecret = WebhookSigning.Secret["whsec_".Length..] });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(withPrefix.Succeeded).IsTrue();
            await Assert.That(without.Succeeded).IsTrue();
        }
    }
}
