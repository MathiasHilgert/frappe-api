namespace Frappe.Kernel.UnitTests;

public sealed class ErrorsTests
{
    private static readonly Error _labelTooLong = Error.Validation(
        "order.tab.label_too_long",
        "Too long."
    );
    private static readonly Error _labelBlank = Error.Validation("order.tab.label_blank", "Blank.");
    private static readonly Error _branchMissing = Error.NotFound(
        "order.tab.branch_missing",
        "Missing."
    );

    [Fact]
    public void Collecting_only_successes_is_a_success()
    {
        ErrorOr<int> first = 1;
        ErrorOr<string> second = "two";

        ErrorOr<Success> result = Errors.Collect(first, second);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public void Collecting_returns_every_error_of_every_result_in_argument_order()
    {
        ErrorOr<int> first = new List<Error> { _labelTooLong, _labelBlank };
        ErrorOr<string> second = "ok";
        ErrorOr<Guid> third = _branchMissing;

        ErrorOr<Success> result = Errors.Collect(first, second, third);

        result.Errors.ShouldBe([_labelTooLong, _labelBlank, _branchMissing]);
    }

    [Fact]
    public void A_pointer_is_added_to_every_error_without_changing_it()
    {
        var withMetadata = Error.Validation(
            "order.tab.label_blank",
            "Blank.",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["max"] = 40 }
        );
        ErrorOr<int> failed = new List<Error> { _labelTooLong, withMetadata };

        ErrorOr<int> result = failed.WithPointer("/label");

        result
            .Errors.Select(e => (e.Code, e.Type, e.Description))
            .ShouldBe([
                (_labelTooLong.Code, _labelTooLong.Type, _labelTooLong.Description),
                (withMetadata.Code, withMetadata.Type, withMetadata.Description),
            ]);
        result.Errors.ShouldAllBe(e => (string)e.Metadata![Errors.PointerKey] == "/label");
        result.Errors[1].Metadata!["max"].ShouldBe(40);
    }

    [Fact]
    public void A_success_passes_through_a_pointer_unchanged()
    {
        ErrorOr<int> succeeded = 7;

        ErrorOr<int> result = succeeded.WithPointer("/label");

        result.Value.ShouldBe(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("label")]
    public void A_pointer_must_start_with_a_slash(string jsonPointer)
    {
        ErrorOr<int> succeeded = 7;

        Action add = () => succeeded.WithPointer(jsonPointer);

        add.ShouldThrow<ArgumentException>();
    }
}
