using Frappe.SharedKernel.Validation;
using Shouldly;

namespace Frappe.SharedKernel.Tests;

public sealed class ResultTests
{
    private static readonly Error First = new("test.first", "First.", "first");
    private static readonly Error Second = new("test.second", "Second.", "second");

    [Fact]
    public void Failure_NotificationChangedAfterwards_KeepsItsErrors()
    {
        var notification = new Notification().Add(First);

        var result = Result.Failure<int>(notification);
        notification.Add(Second).Nest("owner");

        result.Errors.ShouldBe([First]);
    }

    [Fact]
    public void Failure_EmptyNotification_Throws()
    {
        Should.Throw<ArgumentException>(() => Result.Failure<int>(new Notification()));
        Should.Throw<ArgumentException>(() => Result.Failure(new Notification()));
    }

    [Fact]
    public void From_RulesBroken_ReturnsEveryErrorWithoutCreatingTheValue()
    {
        var created = false;
        var notification = new Notification().Add(First).Add(Second);

        var result = Result.From(notification, () => created = true);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First, Second]);
        created.ShouldBeFalse();
    }

    [Fact]
    public void From_NoRulesBroken_ReturnsTheValue()
    {
        var result = Result.From(new Notification(), () => 42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Value_OnFailure_Throws()
    {
        var result = Result.Failure<int>(First);

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
