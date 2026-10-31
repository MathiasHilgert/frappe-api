using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Addresses;

namespace Frappe.Geo.UnitTests.Addresses;

public sealed class PostalCodeFormatTests
{
    private const string Argentina = @"[A-HJ-NP-Z]\d{4}[A-Z]{3}|\d{4}";

    [Theory]
    [InlineData("C1425DQF")]
    [InlineData("c1425dqf")]
    [InlineData("1425")]
    public void An_argentine_cpa_or_legacy_four_digit_code_is_valid(string raw)
    {
        var format = PostalCodeFormat.Create(Argentina, required: false);

        ErrorOr<Success> result = format.Validate(PostalCode.Create(raw).Value);

        result.IsError.ShouldBeFalse();
    }

    // The pattern is anchored at both ends, so a valid code inside a longer value does not pass.
    [Theory]
    [InlineData("ABC")]
    [InlineData("C1425DQFXYZ")]
    [InlineData("X1425")]
    [InlineData("14250")]
    [InlineData("I1425DQF")]
    public void A_code_the_whole_pattern_does_not_match_is_invalid(string raw)
    {
        var format = PostalCodeFormat.Create(Argentina, required: false);

        ErrorOr<Success> result = format.Validate(PostalCode.Create(raw).Value);

        result.FirstError.Code.ShouldBe("geo.postal_code.invalid");
    }

    [Fact]
    public void An_alternative_of_the_pattern_is_anchored_too()
    {
        var format = PostalCodeFormat.Create("1234|ABCD", required: false);

        ErrorOr<Success> result = format.Validate(PostalCode.Create("1234X").Value);

        result.FirstError.Code.ShouldBe("geo.postal_code.invalid");
    }

    [Fact]
    public void Without_a_pattern_any_code_is_valid()
    {
        var format = PostalCodeFormat.Create(null, required: false);

        ErrorOr<Success> result = format.Validate(PostalCode.Create("ANY 123-X").Value);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_code_is_valid_when_the_country_does_not_require_one()
    {
        var format = PostalCodeFormat.Create(Argentina, required: false);

        ErrorOr<Success> result = format.Validate(null);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public void A_missing_code_is_required_when_the_country_requires_one()
    {
        var format = PostalCodeFormat.Create(null, required: true);

        ErrorOr<Success> result = format.Validate(null);

        result.FirstError.Code.ShouldBe("geo.postal_code.required");
    }

    [Fact]
    public void The_pattern_stops_matching_after_one_hundred_milliseconds()
    {
        var format = PostalCodeFormat.Create(Argentina, required: false);

        format.MatchTimeout.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    // A pattern that backtracks past the timeout rejects the code instead of failing the request.
    [Fact]
    public void A_pattern_that_times_out_rejects_the_code()
    {
        var format = PostalCodeFormat.Create("(1|1?)+(1|1?)+(1|1?)+(1|1?)+2", required: false);

        ErrorOr<Success> result = format.Validate(PostalCode.Create("1111111111111111").Value);

        result.FirstError.Code.ShouldBe("geo.postal_code.invalid");
    }
}
