using Frappe.SharedKernel.Validation;
using Shouldly;

namespace Frappe.SharedKernel.Tests;

public sealed class NotificationTests
{
    [Fact]
    public void AddIf_OnlyAddsBrokenRules()
    {
        var notification = new Notification()
            .AddIf(true, new Error("a", "A."))
            .AddIf(false, new Error("b", "B."));

        notification.Errors.Select(e => e.Code).ShouldBe(["a"]);
    }

    [Fact]
    public void Nest_PrefixesFieldsAndNamesFieldlessErrors()
    {
        var notification = new Notification()
            .Add(new Error("a", "A.", "name"))
            .Add(new Error("b", "B."))
            .Nest("owner");

        notification.Errors.Select(e => e.Field).ShouldBe(["owner.name", "owner"]);
    }

    [Fact]
    public void Merge_AppendsTheOtherErrors()
    {
        var notification = new Notification().Add(new Error("a", "A."))
            .Merge(new Notification().Add(new Error("b", "B.")));

        notification.Errors.Select(e => e.Code).ShouldBe(["a", "b"]);
    }
}
