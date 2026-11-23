using Frappe.Messaging.Relay;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>The relay's options are checked at startup, naming the key of a value that cannot work.</summary>
public sealed class OutboxRelayOptionsValidatorTests
{
    [Test]
    public async Task A_sample_interval_that_is_not_positive_stops_the_host_naming_its_key()
    {
        // Arrange
        var validator = new OutboxRelayOptionsValidator();

        var zero = validator.Validate("orders", new OutboxRelayOptions { ConfigurationSection = "Orders:Relay", SampleInterval = TimeSpan.Zero });

        // Act
        var defaults = validator.Validate("orders", new OutboxRelayOptions());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(zero.Failed).IsTrue();
            await Assert.That(zero.FailureMessage).Contains("Configuration 'Orders:Relay:SampleInterval' must be positive");
            await Assert.That(defaults.Succeeded).IsTrue();
        }
    }
}
