using Frappe.Geo.Application.Cities;

namespace Frappe.Geo.UnitTests.Cities;

public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Córdoba", "cordoba")]
    [InlineData("  Villa   María ", "villa maria")]
    [InlineData("Ñorquincó", "norquinco")]
    [InlineData("SAN\tJOSÉ", "san jose")]
    [InlineData("50%", "50%")]
    public void A_text_folds_without_accents_in_lower_case_with_single_spaces(
        string text,
        string folded
    )
    {
        SearchText.Fold(text).ShouldBe(folded);
    }
}
