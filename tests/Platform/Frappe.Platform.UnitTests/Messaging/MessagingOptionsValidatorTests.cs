using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Retention;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>Every messaging option is checked at startup and a rejection names the configuration key of the module that set it.</summary>
public sealed class MessagingOptionsValidatorTests
{
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task A_relay_batch_size_or_attempt_limit_below_one_is_rejected(int value)
    {
        // Act
        var result = new OutboxRelayOptionsValidator().Validate("orders", new OutboxRelayOptions
        {
            ConfigurationSection = "Orders:Relay",
            BatchSize = value,
            MaxAttempts = value,
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.FailureMessage).Contains("Configuration 'Orders:Relay:BatchSize' must be at least 1");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Orders:Relay:MaxAttempts' must be at least 1");
        }
    }

    [Test]
    public async Task A_relay_lease_shorter_than_a_pass_plus_a_publish_and_the_settle_is_rejected()
    {
        // Act
        var result = new OutboxRelayOptionsValidator().Validate(
            null,
            new OutboxRelayOptions
            {
                MaxPassDuration = TimeSpan.FromSeconds(10),
                PublishTimeout = TimeSpan.FromSeconds(5),
                LeaseDuration = TimeSpan.FromSeconds(19),
            });

        // Assert
        await Assert.That(result.FailureMessage)
            .Contains("Configuration 'Messaging:Relay:LeaseDuration' must be at least MaxPassDuration + PublishTimeout + 5 s");
    }

    [Test]
    public async Task An_empty_relay_backoff_is_rejected()
    {
        // Act
        var result = new OutboxRelayOptionsValidator().Validate(null, new OutboxRelayOptions { RetryBackoff = [] });

        // Assert
        await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Relay:RetryBackoff' must not be empty");
    }

    [Test]
    public async Task A_consumer_that_cannot_keep_a_heartbeat_is_rejected_naming_each_key()
    {
        // Act
        var result = new JetStreamConsumerOptionsValidator().Validate("projection", new JetStreamConsumerOptions
        {
            ConfigurationSection = "Messaging:Consumers",
            AckWait = TimeSpan.FromMilliseconds(500),
            MaxDeliveries = 0,
            PrefetchCount = 0,
            Backoff = [],
            MaxHandlerDuration = TimeSpan.FromMilliseconds(100),
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Consumers:AckWait' must be at least 1 s");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Consumers:MaxDeliveries' must be at least 1");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Consumers:PrefetchCount' must be at least 1");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Consumers:Backoff' must not be empty");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Consumers:MaxHandlerDuration' must be at least AckWait");
        }
    }

    [Test]
    public async Task Consumer_defaults_pass()
    {
        // Act
        var result = new JetStreamConsumerOptionsValidator().Validate("projection", new JetStreamConsumerOptions());

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task A_retention_outside_its_bounds_is_rejected_naming_each_key()
    {
        // Act
        var result = new MessageRetentionOptionsValidator().Validate("orders", new MessageRetentionOptions
        {
            ConfigurationSection = "Orders:Retention",
            OutboxDays = 0,
            InboxDays = 1,
            BatchSize = 0,
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.FailureMessage).Contains("Configuration 'Orders:Retention:OutboxDays' must be a whole number of days from 1");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Orders:Retention:InboxDays' must be a whole number of days from");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Orders:Retention:BatchSize' must be at least 1");
        }
    }

    [Test]
    public async Task A_retention_above_ten_years_is_rejected()
    {
        // Act
        var result = new MessageRetentionOptionsValidator().Validate(
            null,
            new MessageRetentionOptions { OutboxDays = MessageRetentionOptions.MaxDays + 1, InboxDays = MessageRetentionOptions.MaxDays + 1 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Retention:OutboxDays'");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Messaging:Retention:InboxDays'");
        }
    }

    [Test]
    public async Task Retention_defaults_pass()
    {
        // Act
        var result = new MessageRetentionOptionsValidator().Validate(null, new MessageRetentionOptions());

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }
}
