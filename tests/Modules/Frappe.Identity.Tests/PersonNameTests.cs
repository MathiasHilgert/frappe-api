using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Users;
using Shouldly;

namespace Frappe.Identity.Tests;

public sealed class PersonNameTests
{
    [Fact]
    public void Create_ValidName_TrimsAndSucceeds()
    {
        var result = PersonName.Create("  Ana  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe("Ana");
    }

    [Fact]
    public void Create_Blank_ReportsNameRequired()
    {
        var result = PersonName.Create("   ");

        result.Errors.ShouldBe([IdentityErrors.NameRequired]);
    }

    [Fact]
    public void Create_TooLong_ReportsNameTooLong()
    {
        var result = PersonName.Create(new string('a', PersonName.MaxLength + 1));

        result.Errors.ShouldBe([IdentityErrors.NameTooLong]);
    }
}
