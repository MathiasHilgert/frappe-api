using Frappe.Results;

namespace Frappe.Platform.UnitTests.Results;

/// <summary>The result type: success and failure, errors, equality and exhaustive switches.</summary>
public sealed class ResultTests
{
    [Test]
    public async Task Success_exposes_its_value()
    {
        // Act
        Result<int> result = 42;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value).IsEqualTo(42);
            await Assert.That(result.Errors).IsEmpty();
        }
    }

    [Test]
    public async Task Failure_exposes_its_errors_and_hides_the_value()
    {
        // Arrange
        Result<int> result = Error.RuleViolation("test.item.date_outside_year", ("year", 2026));

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => _ = result.Value);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors[0]).IsTypeOf<RuleViolationError>();
            await Assert.That(failure).IsNotNull();
        }
    }

    [Test]
    public async Task A_rate_limit_states_its_wait_in_whole_seconds_rounded_up()
    {
        // Act
        var error = new RateLimitedError("test.login.throttled", TimeSpan.FromSeconds(1.01), ("scope", "email"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(error.RetryAfterSeconds).IsEqualTo(2);
            await Assert.That(error.Parameters["retryAfter"]).IsEqualTo(2);
            await Assert.That(error.Parameters["scope"]).IsEqualTo("email");
            await Assert.That(new RateLimitedError("x", TimeSpan.Zero).RetryAfterSeconds).IsEqualTo(1);
            await Assert.That(error).IsEqualTo(new RateLimitedError("test.login.throttled", TimeSpan.FromSeconds(2), ("scope", "email")));
        }
    }

    [Test]
    public async Task A_rate_limit_with_a_negative_wait_is_rejected()
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RateLimitedError("x", TimeSpan.FromSeconds(-1)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Failure_without_errors_is_rejected()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => Result.Failure<int>([]));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task A_failure_case_without_errors_is_rejected()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => _ = new Failure([]));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task A_switch_handles_the_value_and_the_failure_cases()
    {
        // Act
        static string Describe(Result<int> result) => result switch
        {
            Failure failure => $"failed: {failure.Errors[0].Code}",
            int value => $"value: {value}",
        };

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(Describe(42)).IsEqualTo("value: 42");
            await Assert.That(Describe(Error.RuleViolation("test.rejected"))).IsEqualTo("failed: test.rejected");
            await Assert.That(Describe(Result.Failure<int>([Error.InvalidValue("a"), Error.InvalidValue("b")]))).IsEqualTo("failed: a");
        }
    }

    [Test]
    public async Task A_failure_converts_implicitly_and_keeps_every_error()
    {
        // Arrange
        Failure failure = new([Error.InvalidValue("a"), Error.RuleViolation("b")]);

        // Act
        Result<int> result = failure;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors).IsSameReferenceAs(failure.Errors);
            await Assert.That(result is Failure { Errors.Count: 2 }).IsTrue();
        }
    }

    [Test]
    public async Task A_successful_null_is_a_success_whose_contents_match_the_null_pattern()
    {
        // Act
        Result<string?> result = (string?)null;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value).IsNull();
            await Assert.That(result.Errors).IsEmpty();
            await Assert.That(result is null).IsTrue();
            await Assert.That(result is { IsSuccess: true, Value: { } }).IsFalse();
            await Assert.That(result switch { Failure => "failure", string => "text", null => "null" }).IsEqualTo("null");
        }
    }

    [Test]
    public async Task Type_patterns_test_the_contents_not_the_result()
    {
        // Arrange
        Result<string?> text = "AR";

        // Act
        Result<string?> failed = Error.InvalidValue("test.code");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text is string { Length: 2 }).IsTrue();
            await Assert.That(text is Failure).IsFalse();
            await Assert.That(failed is string).IsFalse();
            await Assert.That(failed is not null).IsTrue();
        }
    }

    [Test]
    public async Task Errors_carry_a_code_and_typed_parameters_but_no_human_text()
    {
        // Act
        var error = Error.InvalidValue("test.year.out_of_range", ("min", 1900), ("max", 2100));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(error.Code).IsEqualTo("test.year.out_of_range");
            await Assert.That(error.Parameters["min"]).IsEqualTo(1900);
            await Assert.That(error.Parameters["max"]).IsEqualTo(2100);
        }
    }

    [Test]
    public async Task Duplicated_parameter_names_are_a_bug()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => Error.InvalidValue("test.code", ("min", 1), ("min", 2)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Errors_with_the_same_code_and_parameters_are_equal()
    {
        // Arrange
        var first = Error.InvalidValue("test.year.out_of_range", ("min", 1900), ("max", 2100));

        // Act
        var second = Error.InvalidValue("test.year.out_of_range", ("max", 2100), ("min", 1900));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(second);
            await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
            await Assert.That(first).IsNotEqualTo(Error.InvalidValue("test.year.out_of_range", ("min", 1900), ("max", 2099)));
            await Assert.That((Error)first).IsNotEqualTo(Error.RuleViolation("test.year.out_of_range", ("min", 1900), ("max", 2100)));
        }
    }

    [Test]
    public async Task Array_parameters_compare_by_content()
    {
        // Arrange
        var first = Error.RuleViolation("localization.translation.unavailable", ("available", new[] { "es", "en" }));

        // Act
        var second = Error.RuleViolation("localization.translation.unavailable", ("available", new[] { "es", "en" }));

        // Assert
        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task Nested_sequences_compare_by_content_and_strings_never_equal_char_arrays()
    {
        // Arrange
        var nested = Error.InvalidValue("test.code", ("rows", new[] { new[] { 1, 2 }, [3] }));
        var same = Error.InvalidValue("test.code", ("rows", new[] { new[] { 1, 2 }, [3] }));
        var text = Error.InvalidValue("test.code", ("value", "ab"));

        // Act
        var chars = Error.InvalidValue("test.code", ("value", new[] { 'a', 'b' }));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(nested).IsEqualTo(same);
            await Assert.That(text).IsNotEqualTo(chars);
        }
    }
}
