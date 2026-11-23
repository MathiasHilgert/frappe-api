using Frappe.Scheduling;

namespace Frappe.Platform.UnitTests.Scheduling;

/// <summary>The scheduler's settings are checked at startup, naming the full key (CFG-03).</summary>
public sealed class SchedulingOptionsValidatorTests
{
    [Test]
    public async Task A_missing_connection_string_fails_naming_its_full_key()
    {
        // Act
        var result = new SchedulingOptionsValidator().Validate(null, new SchedulingOptions());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Configuration 'ConnectionStrings:Scheduler' is required.");
        }
    }

    [Test]
    public async Task A_connection_string_passes()
    {
        // Act
        var result = new SchedulingOptionsValidator().Validate(null, new SchedulingOptions { ConnectionString = "Host=db" });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }
}
