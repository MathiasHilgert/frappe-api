using Frappe.Identity.Infrastructure.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>
/// The session lifetimes default to 12 hours idle and 30 days absolute and fail at startup, naming the key, when they cannot hold (S8, CFG-03).
/// </summary>
public sealed class SessionOptionsTests
{
    private static readonly SessionOptionsValidator Validator = new();

    [Test]
    public async Task The_defaults_are_twelve_hours_idle_and_thirty_days_absolute_and_valid()
    {
        // Act
        var options = new SessionOptions();
        var result = Validator.Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.IdleTimeout).IsEqualTo(TimeSpan.FromHours(12));
            await Assert.That(options.AbsoluteTimeout).IsEqualTo(TimeSpan.FromDays(30));
            await Assert.That(result.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task An_idle_timeout_longer_than_the_absolute_one_fails_naming_both_keys()
    {
        // Act
        var result = Validator.Validate(null, new SessionOptions { IdleTimeout = TimeSpan.FromDays(31) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Identity:Sessions:IdleTimeout");
            await Assert.That(result.FailureMessage).Contains("Identity:Sessions:AbsoluteTimeout");
        }
    }

    [Test]
    public async Task A_timeout_that_is_not_positive_fails_naming_its_key()
    {
        // Act
        var result = Validator.Validate(null, new SessionOptions { IdleTimeout = TimeSpan.Zero, AbsoluteTimeout = TimeSpan.FromSeconds(-1) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Identity:Sessions:IdleTimeout");
            await Assert.That(result.FailureMessage).Contains("Identity:Sessions:AbsoluteTimeout");
        }
    }
}
