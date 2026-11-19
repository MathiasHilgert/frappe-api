using Frappe.Persistence;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>The idle-in-transaction timeout may go as low as ten seconds, and no lower.</summary>
public sealed class DatabaseOptionsValidatorTests
{
    private static readonly DatabaseOptionsValidator Validator = new();

    [Test]
    [Arguments("00:00:10")]
    [Arguments("00:00:30")]
    [Arguments("00:01:00")]
    public async Task An_idle_in_transaction_timeout_of_ten_seconds_or_more_is_accepted(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { IdleInTransactionSessionTimeout = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture) });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task An_idle_in_transaction_timeout_under_ten_seconds_is_refused_naming_the_key()
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { IdleInTransactionSessionTimeout = TimeSpan.FromMilliseconds(9_999) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Database:IdleInTransactionSessionTimeout");
        }
    }
}
