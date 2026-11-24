using Frappe.Notifications.Infrastructure.Unsubscribe;

namespace Frappe.Notifications.UnitTests.Preferences;

/// <summary>The signing keys come only from the environment; a deploy without a usable one fails the start, naming the key (S12, CFG-10).</summary>
public sealed class UnsubscribeOptionsTests
{
    private static readonly string Short = Convert.ToBase64String(new byte[16]);

    private static string Failures(UnsubscribeOptions options) =>
        string.Join(' ', new UnsubscribeOptionsValidator().Validate(null, options).Failures ?? []);

    [Test]
    public async Task A_current_key_of_enough_bytes_is_valid_with_retired_keys_beside_it()
    {
        // Arrange
        var options = new UnsubscribeOptions { CurrentKeyId = 2, SigningKey = TestUnsubscribeTokens.KeyOne };
        options.RetiredKeys[1] = TestUnsubscribeTokens.KeyTwo;

        // Act
        var result = new UnsubscribeOptionsValidator().Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not base64!")]
    public async Task A_missing_or_malformed_signing_key_fails_naming_it(string? key)
    {
        // Act
        var message = Failures(new UnsubscribeOptions { SigningKey = key });

        // Assert
        await Assert.That(message).Contains("Notifications:Unsubscribe:SigningKey");
    }

    [Test]
    public async Task A_key_shorter_than_the_mac_fails_naming_it()
    {
        // Act
        var message = Failures(new UnsubscribeOptions { SigningKey = Short });

        // Assert
        await Assert.That(message).Contains("Notifications:Unsubscribe:SigningKey").And.Contains("32 bytes");
    }

    [Test]
    public async Task A_current_id_that_is_not_positive_fails_naming_it()
    {
        // Act
        var message = Failures(new UnsubscribeOptions { CurrentKeyId = 0, SigningKey = TestUnsubscribeTokens.KeyOne });

        // Assert
        await Assert.That(message).Contains("Notifications:Unsubscribe:CurrentKeyId");
    }

    [Test]
    public async Task A_retired_key_that_is_short_or_reuses_the_current_id_fails_naming_it()
    {
        // Arrange
        var options = new UnsubscribeOptions { CurrentKeyId = 2, SigningKey = TestUnsubscribeTokens.KeyOne };
        options.RetiredKeys[2] = TestUnsubscribeTokens.KeyTwo;
        options.RetiredKeys[3] = Short;

        // Act
        var message = Failures(options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(message).Contains("Notifications:Unsubscribe:RetiredKeys:2");
            await Assert.That(message).Contains("Notifications:Unsubscribe:RetiredKeys:3");
        }
    }
}
