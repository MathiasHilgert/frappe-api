using Frappe.Kernel.Authorization;

namespace Frappe.Kernel.UnitTests.Authorization;

public sealed class PolicyTests
{
    [Theory]
    [InlineData("order.tab.open")]
    [InlineData("business.manage")]
    [InlineData("cash_box.shift.close_2")]
    public void A_lowercase_dotted_permission_is_accepted(string permission)
    {
        Policy policy = new(permission);

        policy.Permission.ShouldBe(permission);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("order")]
    [InlineData("Order.Tab.Open")]
    [InlineData("order..open")]
    [InlineData("order.tab.")]
    [InlineData(".order.tab")]
    [InlineData("order.tab.open\n")]
    [InlineData("order-tab.open")]
    [InlineData("1order.tab")]
    [InlineData("order.tab.ópen")]
    [InlineData("$self")]
    [InlineData("$anonymous")]
    public void Anything_else_is_rejected(string permission)
    {
        Action create = () => _ = new Policy(permission);

        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Self_and_anonymous_are_distinct_reserved_policies()
    {
        bool equal = Policy.Self == Policy.Anonymous;

        equal.ShouldBeFalse();
    }
}
