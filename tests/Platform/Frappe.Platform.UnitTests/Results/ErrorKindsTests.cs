using Frappe.Results;

namespace Frappe.Platform.UnitTests.Results;

/// <summary>Each kind of error keeps its code and parameters, and errors compare by content.</summary>
public sealed class ErrorKindsTests
{
    [Test]
    public async Task Each_kind_of_error_keeps_its_code_and_its_parameters()
    {
        // Act
        Error[] errors =
        [
            new ConflictError("test.item.duplicate", ("year", 2026)),
            new ForbiddenError("test.session.forbidden", ("year", 2026)),
            new NotFoundError("test.item.not_found", ("year", 2026)),
            new PreconditionFailedError("test.item.stale", ("year", 2026)),
            new UnauthenticatedError("test.session.missing", ("year", 2026)),
            new UnavailableError("test.provider.down", ("year", 2026)),
        ];

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(errors.Select(e => e.GetType())).IsEquivalentTo(
                [
                    typeof(ConflictError), typeof(ForbiddenError), typeof(NotFoundError),
                    typeof(PreconditionFailedError), typeof(UnauthenticatedError), typeof(UnavailableError),
                ]);
            await Assert.That(errors.Select(e => e.Code)).IsEquivalentTo(
                [
                    "test.item.duplicate", "test.session.forbidden", "test.item.not_found",
                    "test.item.stale", "test.session.missing", "test.provider.down",
                ]);
            await Assert.That(errors.All(e => (int)e.Parameters["year"]! == 2026)).IsTrue();
        }
    }

    [Test]
    public async Task Errors_with_the_same_sequence_parameters_are_equal_and_a_different_element_or_length_breaks_it()
    {
        // Arrange
        var left = new ConflictError("test.item.duplicate", ("days", new[] { 1, 2 }), ("note", null));
        var same = new ConflictError("test.item.duplicate", ("days", new[] { 1, 2 }), ("note", null));
        var otherElement = new ConflictError("test.item.duplicate", ("days", new[] { 1, 3 }), ("note", null));
        var otherLength = new ConflictError("test.item.duplicate", ("days", new[] { 1, 2, 3 }), ("note", null));
        var otherKind = new NotFoundError("test.item.duplicate", ("days", new[] { 1, 2 }), ("note", null));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(left).IsEqualTo(same);
            await Assert.That(left.GetHashCode()).IsEqualTo(same.GetHashCode());
            await Assert.That(left).IsNotEqualTo(otherElement);
            await Assert.That(left).IsNotEqualTo(otherLength);
            await Assert.That(left.Equals((Error)otherKind)).IsFalse();
        }
    }

    [Test]
    public async Task A_successful_result_holding_null_matches_the_null_pattern_and_a_failure_does_not()
    {
        // Arrange
        Result<string?> success = (string?)null;
        Result<string?> failure = new NotFoundError("test.item.not_found");

        // Act
        var successFlag = success.IsSuccess;
        var successIsNull = success is null;
        var failureIsNull = failure is null;
        var failureIsFailure = failure is Failure;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(successFlag).IsTrue();
            await Assert.That(successIsNull).IsTrue();
            await Assert.That(failureIsNull).IsFalse();
            await Assert.That(failureIsFailure).IsTrue();
        }
    }
}
