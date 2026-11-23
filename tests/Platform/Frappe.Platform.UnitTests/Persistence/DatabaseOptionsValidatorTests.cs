using Frappe.Persistence;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>
/// Database limits are checked at startup, naming each key: the pool, the command and statement timeouts, and an
/// idle-in-transaction timeout that may go as low as ten seconds and no lower.
/// </summary>
public sealed class DatabaseOptionsValidatorTests
{
    private static readonly DatabaseOptionsValidator Validator = new();

    private static TimeSpan Parse(string value) => TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Test]
    [Arguments("00:00:10")]
    [Arguments("00:00:30")]
    [Arguments("00:01:00")]
    public async Task An_idle_in_transaction_timeout_of_ten_seconds_or_more_is_accepted(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { IdleInTransactionSessionTimeout = Parse(value) });

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

    [Test]
    public async Task The_defaults_are_accepted()
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions());

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(1)]
    [Arguments(6)]
    [Arguments(DatabaseOptions.MaxPoolSizeLimit)]
    public async Task A_pool_from_one_connection_to_the_limit_is_accepted(int size)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { MaxPoolSize = size });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(DatabaseOptions.MaxPoolSizeLimit + 1)]
    public async Task A_pool_outside_one_to_the_limit_is_refused_naming_the_key(int size)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { MaxPoolSize = size });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Configuration 'Database:MaxPoolSize'");
        }
    }

    [Test]
    [Arguments("00:00:01")]
    [Arguments("00:00:35")]
    [Arguments("01:00:00")]
    public async Task A_command_timeout_of_whole_seconds_between_one_second_and_one_hour_is_accepted(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { CommandTimeout = Parse(value) });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("00:00:00")]
    [Arguments("00:00:00.999")]
    [Arguments("00:00:02.500")]
    [Arguments("01:00:01")]
    public async Task A_command_timeout_that_is_zero_fractional_or_beyond_an_hour_is_refused_naming_the_key(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { CommandTimeout = Parse(value) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Configuration 'Database:CommandTimeout'");
        }
    }

    [Test]
    [Arguments("00:00:01")]
    [Arguments("00:00:02.500")]
    [Arguments("01:00:00")]
    public async Task A_statement_timeout_of_whole_milliseconds_between_one_second_and_one_hour_is_accepted(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { StatementTimeout = Parse(value) });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("00:00:00")]
    [Arguments("00:00:00.999")]
    [Arguments("02:00:00")]
    public async Task A_statement_timeout_that_is_zero_or_outside_one_second_to_one_hour_is_refused_naming_the_key(string value)
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { StatementTimeout = Parse(value) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Configuration 'Database:StatementTimeout'");
        }
    }

    [Test]
    public async Task A_timeout_finer_than_a_millisecond_is_refused()
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions { StatementTimeout = TimeSpan.FromSeconds(2) + TimeSpan.FromTicks(1) });

        // Assert
        await Assert.That(result.Failed).IsTrue();
    }

    [Test]
    public async Task Every_failing_key_is_reported_at_once()
    {
        // Act
        var result = Validator.Validate(null, new DatabaseOptions
        {
            MaxPoolSize = 0,
            CommandTimeout = TimeSpan.Zero,
            StatementTimeout = TimeSpan.Zero,
            IdleInTransactionSessionTimeout = TimeSpan.Zero,
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failures!.Count()).IsEqualTo(4);
            await Assert.That(result.FailureMessage).Contains("Database:MaxPoolSize");
            await Assert.That(result.FailureMessage).Contains("Database:CommandTimeout");
            await Assert.That(result.FailureMessage).Contains("Database:StatementTimeout");
            await Assert.That(result.FailureMessage).Contains("Database:IdleInTransactionSessionTimeout");
        }
    }
}
