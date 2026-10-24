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
        var notification = new ValidationNotification().Add(First);

        var result = Result.Failure<int>(notification);
        notification.Add(Second).Nest("owner");

        result.Errors.ShouldBe([First]);
    }

    [Fact]
    public void Failure_EmptyNotification_Throws()
    {
        Should.Throw<ArgumentException>(() => Result.Failure<int>(new ValidationNotification()));
        Should.Throw<ArgumentException>(() => Result.Failure(new ValidationNotification()));
    }

    [Fact]
    public void From_RulesBroken_ReturnsEveryErrorWithoutCreatingTheValue()
    {
        var created = false;
        var notification = new ValidationNotification().Add(First).Add(Second);

        var result = Result.From(notification, () => created = true);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldBe([First, Second]);
        created.ShouldBeFalse();
    }

    [Fact]
    public void From_NoRulesBroken_ReturnsTheValue()
    {
        var result = Result.From(new ValidationNotification(), () => 42);

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
    public void Errors_ByRule_AreEqual_WhateverTheOccurrenceDetails()
    {
        var rule = new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests);

        var occurrence = rule.WithRetryAfter(TimeSpan.FromSeconds(5)).WithMetadata("ownerId", Guid.CreateVersion7());
        var annotated = rule.WithMetadata("ownerId", Guid.CreateVersion7());

        Error.ByRule.Equals(occurrence, rule).ShouldBeTrue();
        occurrence.Is(rule).ShouldBeTrue();
        Error.ByRule.Equals(annotated, occurrence).ShouldBeTrue();
        Error.ByRule.GetHashCode(occurrence).ShouldBe(Error.ByRule.GetHashCode(rule));
        new[] { occurrence }.ShouldContain(rule, Error.ByRule);
    }

    [Fact]
    public void Errors_ByRule_Differ_WhenTheFieldDiffers() =>
        Error.ByRule.Equals(new Error("sample.one", "Same.", "a"), new Error("sample.one", "Same.", "b")).ShouldBeFalse();

    [Fact]
    public void Errors_UseDefaultRecordEquality()
    {
        var rule = new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests);

        rule.ShouldBe(new Error("sample.slow", "Slow down.", Kind: ErrorKind.TooManyRequests));
        (rule.WithRetryAfter(TimeSpan.FromSeconds(5)) == rule).ShouldBeFalse();
        (rule with { Message = "Other." }).ShouldNotBe(rule);
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
        var result = Result.Success<string?>("value");

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

    [Fact]
    public void Failure_FromAnotherResult_PropagatesEveryError()
    {
        var invalid = Result.Failure<string>(new ValidationNotification().Add(First).Add(Second));

        var typed = Result.Failure<int>(invalid.Errors);
        var untyped = Result.Failure(invalid.Errors);

        typed.Errors.ShouldBe([First, Second]);
        untyped.Errors.ShouldBe([First, Second]);
    }

    [Fact]
    public void Failure_FromNoErrors_Throws()
    {
        Should.Throw<ArgumentException>(() => Result.Failure<int>(Array.Empty<Error>()));
        Should.Throw<ArgumentException>(() => Result.Failure(Array.Empty<Error>()));
    }

    [Fact]
    public void Failure_FromErrors_KeepsASnapshot()
    {
        var errors = new List<Error> { First };

        var result = Result.Failure(errors);
        errors.Add(Second);

        result.Errors.ShouldBe([First]);
    }

    [Fact]
    public void ValueResult_DoesNotDeriveFromResult_SoItsFlagIsNeverShadowed()
    {
        typeof(Result<int>).BaseType.ShouldBe(typeof(object));
        typeof(Result<int>).GetProperty(nameof(Result<int>.IsSuccess))!.DeclaringType.ShouldBe(typeof(Result<int>));
    }

    [Fact]
    public void WithoutValue_KeepsTheOutcome()
    {
        Result.Success(5).WithoutValue().IsSuccess.ShouldBeTrue();
        Result.Failure<int>(First).WithoutValue().Errors.ShouldBe([First]);
    }

    [Fact]
    public void Has_FindsARuleAmongEveryError()
    {
        var result = Result.Failure(new ValidationNotification().Add(First).Add(Second));

        result.Has(Second).ShouldBeTrue();
        Result.Success().Has(First).ShouldBeFalse();
        Result.Failure<int>(First).Has(Second).ShouldBeFalse();
    }
}
