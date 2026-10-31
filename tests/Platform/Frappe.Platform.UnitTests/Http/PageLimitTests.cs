using Frappe.Platform.Http.Paging;

namespace Frappe.Platform.UnitTests.Http;

public sealed class PageLimitTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(120, 120)]
    [InlineData(201, 200)]
    public void The_limit_defaults_to_50_and_is_clamped_to_1_through_200(int? limit, int expected)
    {
        PageLimit.Of(limit).ShouldBe(expected);
    }
}
