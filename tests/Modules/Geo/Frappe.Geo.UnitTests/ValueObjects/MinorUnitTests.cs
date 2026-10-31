using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.UnitTests.ValueObjects;

public sealed class MinorUnitTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void Zero_to_four_digits_are_accepted(int digits)
    {
        ErrorOr<MinorUnit> result = MinorUnit.Create(digits);

        result.Value.Value.ShouldBe(digits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Digits_outside_zero_to_four_are_rejected(int digits)
    {
        ErrorOr<MinorUnit> result = MinorUnit.Create(digits);

        result.FirstError.Code.ShouldBe("geo.minor_unit.invalid");
    }

    [Fact]
    public void Units_with_the_same_digits_are_equal()
    {
        MinorUnit first = MinorUnit.Create(2).Value;

        MinorUnit second = MinorUnit.Create(2).Value;

        first.ShouldBe(second);
    }

    [Fact]
    public void The_text_form_is_the_digit_count()
    {
        MinorUnit unit = MinorUnit.Create(3).Value;

        string text = unit.ToString();

        text.ShouldBe("3");
    }
}
