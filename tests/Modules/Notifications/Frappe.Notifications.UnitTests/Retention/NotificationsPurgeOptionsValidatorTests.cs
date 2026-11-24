using Frappe.Notifications.Infrastructure.Retention;

namespace Frappe.Notifications.UnitTests.Retention;

/// <summary>The purge settings default to the spec's windows and fail the start naming the key (S11, CFG-01).</summary>
public sealed class NotificationsPurgeOptionsValidatorTests
{
    private static readonly NotificationsPurgeOptionsValidator Validator = new();

    [Test]
    public async Task The_defaults_are_ninety_days_of_deliveries_seven_of_provider_events_and_batches_of_a_thousand_and_valid()
    {
        // Act
        var options = new NotificationsPurgeOptions();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.DeliveryDays).IsEqualTo(90);
            await Assert.That(options.ProviderEventDays).IsEqualTo(7);
            await Assert.That(options.BatchSize).IsEqualTo(1000);
            await Assert.That(Validator.Validate(null, options).Succeeded).IsTrue();
        }
    }

    [Test]
    [Arguments("DeliveryDays", 0)]
    [Arguments("DeliveryDays", 3651)]
    [Arguments("ProviderEventDays", 0)]
    [Arguments("ProviderEventDays", -1)]
    [Arguments("SendRequestDays", 0)]
    [Arguments("BatchSize", 0)]
    public async Task A_value_out_of_range_fails_naming_its_full_key(string key, int value)
    {
        // Arrange
        var options = new NotificationsPurgeOptions();
        typeof(NotificationsPurgeOptions).GetProperty(key)!.SetValue(options, value);

        // Act
        var result = Validator.Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains($"Configuration 'Notifications:Purge:{key}'").And.Contains($"'{value}'");
        }
    }
}
