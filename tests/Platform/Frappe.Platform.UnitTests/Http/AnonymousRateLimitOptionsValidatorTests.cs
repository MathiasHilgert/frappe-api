using Frappe.Http;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The rate limit is checked at startup, every failure naming its full key (CFG-03).</summary>
public sealed class AnonymousRateLimitOptionsValidatorTests
{
    [Test]
    public async Task A_permit_limit_under_one_and_a_window_that_is_not_positive_are_both_reported()
    {
        // Arrange
        var validator = new AnonymousRateLimitOptionsValidator(Options.DefaultName, "Http:RateLimit");

        // Act
        var result = validator.Validate(Options.DefaultName, new AnonymousRateLimitOptions { PermitLimit = 0, Window = TimeSpan.Zero });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:RateLimit:PermitLimit' must be at least 1.");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:RateLimit:Window' must be positive");
        }
    }

    [Test]
    public async Task A_policy_names_its_own_section_and_the_defaults_pass()
    {
        // Arrange
        var validator = new AnonymousRateLimitOptionsValidator("export", "Orders:Export:RateLimit");

        var invalid = validator.Validate("export", new AnonymousRateLimitOptions { PermitLimit = -1 });
        var defaults = validator.Validate("export", new AnonymousRateLimitOptions());

        // Act
        var other = validator.Validate(Options.DefaultName, new AnonymousRateLimitOptions { PermitLimit = -1 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(invalid.FailureMessage).Contains("Configuration 'Orders:Export:RateLimit:PermitLimit'");
            await Assert.That(defaults.Succeeded).IsTrue();
            await Assert.That(other.Skipped).IsTrue();
        }
    }
}
