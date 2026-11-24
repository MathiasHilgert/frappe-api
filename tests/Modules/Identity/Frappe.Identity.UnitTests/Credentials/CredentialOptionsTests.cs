using Frappe.Identity.Infrastructure.Credentials;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>The password and breach check settings default to production values and name their full key when wrong (CFG-02, CFG-03).</summary>
public sealed class CredentialOptionsTests
{
    [Test]
    public async Task The_defaults_are_valid_and_are_owasps_parameters_and_a_one_second_timeout()
    {
        // Arrange
        var passwords = new PasswordOptions();
        var breach = new BreachCheckOptions();

        // Act
        var results = (new PasswordOptionsValidator().Validate(null, passwords), new BreachCheckOptionsValidator().Validate(null, breach));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(results.Item1.Succeeded).IsTrue();
            await Assert.That(results.Item2.Succeeded).IsTrue();
            await Assert.That((passwords.Iterations, passwords.MemoryMebibytes)).IsEqualTo((2, 19));
            await Assert.That(breach.BaseUrl.AbsoluteUri).IsEqualTo("https://api.pwnedpasswords.com/");
            await Assert.That(breach.Timeout).IsEqualTo(TimeSpan.FromSeconds(1));
        }
    }

    [Test]
    public async Task Hashing_parameters_out_of_range_are_refused_naming_every_key()
    {
        // Act
        var result = new PasswordOptionsValidator().Validate(null, new PasswordOptions { Iterations = 0, MemoryMebibytes = 7 });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(string.Join(' ', result.Failures!)).Contains("Identity:Passwords:Iterations");
            await Assert.That(string.Join(' ', result.Failures!)).Contains("Identity:Passwords:MemoryMebibytes");
        }
    }

    [Test]
    public async Task A_memory_above_the_ceiling_is_refused()
    {
        // Act
        var result = new PasswordOptionsValidator().Validate(null, new PasswordOptions { MemoryMebibytes = PasswordOptions.MaxMemoryMebibytes + 1 });

        // Assert
        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task A_breach_check_with_a_bad_timeout_break_or_address_is_refused_naming_every_key()
    {
        // Arrange
        var options = new BreachCheckOptions
        {
            Timeout = TimeSpan.Zero,
            BreakDuration = TimeSpan.FromHours(1),
            BaseUrl = new Uri("ftp://example.com/"),
        };

        // Act
        var result = new BreachCheckOptionsValidator().Validate(null, options);

        // Assert
        var failures = string.Join(' ', result.Failures ?? []);
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(failures).Contains("Identity:BreachCheck:Timeout");
            await Assert.That(failures).Contains("Identity:BreachCheck:BreakDuration");
            await Assert.That(failures).Contains("Identity:BreachCheck:BaseUrl");
        }
    }

    [Test]
    public async Task A_missing_address_is_refused()
    {
        // Act
        var result = new BreachCheckOptionsValidator().Validate(null, new BreachCheckOptions { BaseUrl = null! });

        // Assert
        await Assert.That(string.Join(' ', result.Failures ?? [])).Contains("Identity:BreachCheck:BaseUrl");
    }
}
