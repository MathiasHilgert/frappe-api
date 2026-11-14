using Frappe.Results;

namespace Frappe.Platform.Tests.Results;

public sealed class NotificationTests
{
    private static Result<string> Code(string raw) =>
        raw.Length == 2
            ? Result.Success(raw)
            : Error.InvalidValue("geo.country_code.invalid_length", ("length", 2));

    private static Result<int> Year(int raw) =>
        raw is >= 1900 and <= 2100
            ? Result.Success(raw)
            : Error.InvalidValue("geo.holiday_year.out_of_range", ("min", 1900), ("max", 2100));

    [Test]
    public async Task Collects_every_error_instead_of_stopping_at_the_first()
    {
        var notification = new Notification();

        notification.Collect(Code("ARG"));
        notification.Collect(Year(3000));

        await Assert.That(notification.HasErrors).IsTrue();
        await Assert.That(notification.Errors.Select(e => e.Code))
            .IsEquivalentTo(["geo.country_code.invalid_length", "geo.holiday_year.out_of_range"]);
    }

    [Test]
    public async Task Returns_the_values_of_successful_results()
    {
        var notification = new Notification();

        var code = notification.Collect(Code("AR"));
        var year = notification.Collect(Year(2026));

        await Assert.That(notification.HasErrors).IsFalse();
        await Assert.That(code).IsEqualTo("AR");
        await Assert.That(year).IsEqualTo(2026);
    }

    [Test]
    public async Task Converts_into_a_failed_result_carrying_all_errors()
    {
        var notification = new Notification();
        notification.Collect(Code("A"));
        notification.Collect(Year(1));

        Result<string> result = notification.ToFailure<string>();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);
    }

    [Test]
    public void Converting_a_clean_notification_into_a_failure_is_a_bug()
    {
        var notification = new Notification();

        Assert.Throws<InvalidOperationException>(() => notification.ToFailure<string>());
    }

    [Test]
    public async Task Combine_returns_every_valid_value_together()
    {
        var input = Result.Combine(Code("AR"), Year(2026));

        var (code, year) = input.Value;
        await Assert.That(code).IsEqualTo("AR");
        await Assert.That(year).IsEqualTo(2026);
    }

    [Test]
    public async Task Combine_reports_every_error_of_every_input()
    {
        var input = Result.Combine(Code("ARG"), Year(3000), Code("X"));

        await Assert.That(input.IsSuccess).IsFalse();
        await Assert.That(input.Errors.Count).IsEqualTo(3);
    }
}
