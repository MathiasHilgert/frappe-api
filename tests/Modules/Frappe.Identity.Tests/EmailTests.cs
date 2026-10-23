using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Shouldly;

namespace Frappe.Identity.Tests;

public sealed class EmailTests
{
    [Fact]
    public void Create_ValidEmail_TrimsAndSucceeds()
    {
        var result = Email.Create("  ana@example.com ");

        result.Value.Value.ShouldBe("ana@example.com");
    }

    [Fact]
    public void Create_DiffersOnlyInCase_IsEqual() =>
        Email.Create("Ana@Example.com").Value.ShouldBe(Email.Create("ana@example.com").Value);

    [Fact]
    public void Create_Blank_ReportsEmailRequired() =>
        Email.Create(" ").Errors.ShouldBe([IdentityErrors.EmailRequired]);

    [Theory]
    [InlineData("ana")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@@example.com")]
    public void Create_Malformed_ReportsEmailInvalid(string value) =>
        Email.Create(value).Errors.ShouldBe([IdentityErrors.EmailInvalid]);

    [Fact]
    public void Create_TooLong_ReportsEmailInvalid() =>
        Email.Create(new string('a', Email.MaxLength) + "@x.io").Errors.ShouldBe([IdentityErrors.EmailInvalid]);
}
