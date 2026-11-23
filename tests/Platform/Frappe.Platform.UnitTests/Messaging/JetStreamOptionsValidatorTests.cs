using Frappe.Messaging.JetStream;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>The NATS settings are checked at startup, naming the full key (CFG-03).</summary>
public sealed class JetStreamOptionsValidatorTests
{
    [Test]
    public async Task A_missing_url_fails_naming_its_full_key()
    {
        // Act
        var result = new JetStreamOptionsValidator().Validate(null, new JetStreamOptions());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Configuration 'ConnectionStrings:Nats' is required.");
        }
    }

    [Test]
    public async Task A_url_passes()
    {
        // Act
        var result = new JetStreamOptionsValidator().Validate(null, new JetStreamOptions { Url = "nats://localhost:4222" });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }
}
