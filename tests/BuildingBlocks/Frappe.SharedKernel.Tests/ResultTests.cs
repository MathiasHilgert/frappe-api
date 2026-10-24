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

    [Fact]
    public void WithRetryAfter_CarriesTheWait_AndKeepsTheRule()
    {
        var error = new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests);

        var waiting = error.WithRetryAfter(TimeSpan.FromSeconds(5));

        waiting.RetryAfter.ShouldBe(TimeSpan.FromSeconds(5));
        waiting.Code.ShouldBe(error.Code);
        waiting.Kind.ShouldBe(ErrorKind.TooManyRequests);
        error.RetryAfter.ShouldBeNull();
    }

    [Fact]
    public void WithMetadata_AddsAnEntry_WithoutTouchingTheOriginal()
    {
        var error = new Error("sample.refused", "Refused.");
        var id = Guid.NewGuid();

        var tagged = error.WithMetadata("ownerId", id).WithMetadata("extra", 1);

        tagged.Metadata.ShouldNotBeNull()["ownerId"].ShouldBe(id);
        tagged.Metadata["extra"].ShouldBe(1);
        error.Metadata.ShouldBeNull();
    }

    [Fact]
    public void Errors_WithTheSameCode_AreEqual_WhateverTheOccurrenceDetails()
    {
        var rule = new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests);

        var occurrence = rule.WithRetryAfter(TimeSpan.FromSeconds(5)).WithMetadata("ownerId", Guid.CreateVersion7());
        var annotated = rule.WithMetadata("ownerId", Guid.CreateVersion7());

        occurrence.ShouldBe(rule);
        (occurrence == rule).ShouldBeTrue();
        annotated.ShouldBe(occurrence);
        occurrence.GetHashCode().ShouldBe(rule.GetHashCode());
    }

    [Fact]
    public void Errors_WithDifferentCodes_AreNotEqual_EvenWithTheSameMessage() =>
        new Error("sample.one", "Same.").ShouldNotBe(new Error("sample.two", "Same."));

    [Fact]
    public void Errors_WithTheSameCode_OnDifferentFields_AreNotEqual() =>
        new Error("sample.required", "Required.", "name").ShouldNotBe(new Error("sample.required", "Required.", "email"));

    [Fact]
    public void TryGetValue_OnSuccess_ReturnsTheValue()
    {
        Result<string> result = "built";

        result.TryGetValue(out var value).ShouldBeTrue();
        value.ShouldBe("built");
    }

    [Fact]
    public void TryGetValue_OnFailure_ReturnsFalse()
    {
        Result<string> result = First;

        result.TryGetValue(out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void ImplicitConversion_FromError_IsAFailureWithThatError()
    {
        Result<int> result = First;

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First]);
    }

    [Fact]
    public void IsSuccess_LetsTheCompilerTrustValue()
    {
        Result<string?> result = Result.Success<string?>("value");

        // MemberNotNullWhen(true, Value): no null-forgiving operator needed inside the branch.
        if (result.IsSuccess)
            result.Value.Length.ShouldBe(5);
    }

    [Fact]
    public void Match_PicksTheBranchForTheOutcome()
    {
        Result<int> success = 21;
        Result<int> failure = Second;

        success.Match(v => v * 2, _ => -1).ShouldBe(42);
        failure.Match(v => v * 2, errors => errors.Count).ShouldBe(1);
    }
}
