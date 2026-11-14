using Frappe.Results;

namespace Frappe.Platform.Tests.Results;

public sealed class ResultTests
{
    [Test]
    public async Task Success_exposes_its_value()
    {
        Result<int> result = 42;

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(42);
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task Failure_exposes_its_errors_and_hides_the_value()
    {
        Result<int> result = Error.RuleViolation("geo.holiday.date_outside_year", ("year", 2026));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors[0]).IsTypeOf<RuleViolationError>();
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Test]
    public void Failure_without_errors_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure<int>([]));
    }

    [Test]
    public void A_failure_case_without_errors_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => _ = new Failure([]));
    }

    [Test]
    public async Task A_switch_handles_the_value_and_the_failure_cases()
    {
        static string Describe(Result<int> result) => result switch
        {
            Failure failure => $"failed: {failure.Errors[0].Code}",
            int value => $"value: {value}",
        };

        await Assert.That(Describe(42)).IsEqualTo("value: 42");
        await Assert.That(Describe(Error.RuleViolation("test.rejected"))).IsEqualTo("failed: test.rejected");
        await Assert.That(Describe(Result.Failure<int>([Error.InvalidValue("a"), Error.InvalidValue("b")]))).IsEqualTo("failed: a");
    }

    [Test]
    public async Task A_failure_converts_implicitly_and_keeps_every_error()
    {
        Failure failure = new([Error.InvalidValue("a"), Error.RuleViolation("b")]);
        Result<int> result = failure;

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsSameReferenceAs(failure.Errors);
        await Assert.That(result is Failure { Errors.Count: 2 }).IsTrue();
    }

    [Test]
    public async Task A_successful_null_is_a_success_whose_contents_match_the_null_pattern()
    {
        Result<string?> result = (string?)null;

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result is null).IsTrue();
        await Assert.That(result is { IsSuccess: true, Value: { } }).IsFalse();
        await Assert.That(result switch { Failure => "failure", string => "text", null => "null" }).IsEqualTo("null");
    }

    [Test]
    public async Task Type_patterns_test_the_contents_not_the_result()
    {
        Result<string?> text = "AR";
        Result<string?> failed = Error.InvalidValue("test.code");

        await Assert.That(text is string { Length: 2 }).IsTrue();
        await Assert.That(text is Failure).IsFalse();
        await Assert.That(failed is string).IsFalse();
        await Assert.That(failed is not null).IsTrue();
    }

    [Test]
    public async Task Errors_carry_a_code_and_typed_parameters_but_no_human_text()
    {
        var error = Error.InvalidValue("geo.holiday_year.out_of_range", ("min", 1900), ("max", 2100));

        await Assert.That(error.Code).IsEqualTo("geo.holiday_year.out_of_range");
        await Assert.That(error.Parameters["min"]).IsEqualTo(1900);
        await Assert.That(error.Parameters["max"]).IsEqualTo(2100);
    }

    [Test]
    public void Duplicated_parameter_names_are_a_bug()
    {
        Assert.Throws<ArgumentException>(() => Error.InvalidValue("test.code", ("min", 1), ("min", 2)));
    }

    [Test]
    public async Task Errors_with_the_same_code_and_parameters_are_equal()
    {
        var first = Error.InvalidValue("geo.holiday_year.out_of_range", ("min", 1900), ("max", 2100));
        var second = Error.InvalidValue("geo.holiday_year.out_of_range", ("max", 2100), ("min", 1900));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
        await Assert.That(first).IsNotEqualTo(Error.InvalidValue("geo.holiday_year.out_of_range", ("min", 1900), ("max", 2099)));
        await Assert.That((Error)first).IsNotEqualTo(Error.RuleViolation("geo.holiday_year.out_of_range", ("min", 1900), ("max", 2100)));
    }

    [Test]
    public async Task Array_parameters_compare_by_content()
    {
        var first = Error.RuleViolation("localization.translation.unavailable", ("available", new[] { "es", "en" }));
        var second = Error.RuleViolation("localization.translation.unavailable", ("available", new[] { "es", "en" }));

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task Nested_sequences_compare_by_content_and_strings_never_equal_char_arrays()
    {
        var nested = Error.InvalidValue("test.code", ("rows", new[] { new[] { 1, 2 }, new[] { 3 } }));
        var same = Error.InvalidValue("test.code", ("rows", new[] { new[] { 1, 2 }, new[] { 3 } }));
        var text = Error.InvalidValue("test.code", ("value", "ab"));
        var chars = Error.InvalidValue("test.code", ("value", new[] { 'a', 'b' }));

        await Assert.That(nested).IsEqualTo(same);
        await Assert.That(text).IsNotEqualTo(chars);
    }
}
