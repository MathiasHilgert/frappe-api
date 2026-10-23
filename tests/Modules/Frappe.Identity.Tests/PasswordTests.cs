using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Shouldly;

namespace Frappe.Identity.Tests;

public sealed class PasswordTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(128)]
    public void Create_LengthWithinBounds_Succeeds(int length)
    {
        var result = Password.Create(new string('a', length));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Length.ShouldBe(length);
    }

    [Fact]
    public void Create_NineCharacters_ReportsTooShort()
    {
        Password.Create(new string('a', 9)).Errors.ShouldBe([IdentityErrors.PasswordTooShort]);
    }

    [Fact]
    public void Create_Missing_ReportsTooShort()
    {
        Password.Create(null).Errors.ShouldBe([IdentityErrors.PasswordTooShort]);
    }

    [Fact]
    public void Create_129Characters_ReportsTooLong()
    {
        Password.Create(new string('a', 129)).Errors.ShouldBe([IdentityErrors.PasswordTooLong]);
    }

    [Fact]
    public void Create_TenEmoji_CountsCodePointsNotUtf16Units()
    {
        // Ten astral code points are twenty UTF-16 units; the policy counts what the person typed.
        var tenEmoji = string.Concat(Enumerable.Repeat("\U0001F347", 10));
        var nineEmoji = string.Concat(Enumerable.Repeat("\U0001F347", 9));

        Password.Create(tenEmoji).IsSuccess.ShouldBeTrue();
        Password.Create(nineEmoji).Errors.ShouldBe([IdentityErrors.PasswordTooShort]);
        Password.Create(string.Concat(Enumerable.Repeat("\U0001F347", 128))).IsSuccess.ShouldBeTrue();
        Password.Create(string.Concat(Enumerable.Repeat("\U0001F347", 129))).Errors.ShouldBe([IdentityErrors.PasswordTooLong]);
    }

    [Fact]
    public void Create_NoCompositionRules_AcceptsSpacesAndKeepsThem()
    {
        var result = Password.Create("  all lowercase words  ");

        result.Value.Value.ShouldBe("  all lowercase words  ");
    }

    [Fact]
    public void ToString_NeverRevealsThePassword()
    {
        Password.Create("correct horse battery").Value.ToString().ShouldNotContain("horse");
    }
}
