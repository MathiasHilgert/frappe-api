using Frappe.SharedKernel.Localization;
using Shouldly;

namespace Frappe.SharedKernel.Tests;

public sealed class LocaleTests
{
    [Theory]
    [InlineData("es-AR", "es-AR")]
    [InlineData("es_ar", "es-AR")]
    [InlineData("ES-AR", "es-AR")]
    [InlineData("EN", "en")]
    [InlineData("zh-hant-tw", "zh-Hant-TW")]
    [InlineData("es-419", "es-419")]
    [InlineData(" pt-br ", "pt-BR")]
    [InlineData("de-CH-1996", "de-CH-1996")]
    public void Create_CanonicalisesTheTag(string input, string expected)
    {
        var result = Locale.Create(input);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
        result.Value.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Language_IsTheBaseSubtag() => Locale.Parse("zh-Hant-TW").Language.ShouldBe("zh");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("languages-US")]
    [InlineData("es--AR")]
    [InlineData("es-AR-")]
    [InlineData("es-A")]
    [InlineData("1s-AR")]
    [InlineData("es-AR-x")]
    [InlineData("*")]
    public void Create_RejectsStructurallyInvalidTags(string? input)
    {
        var result = Locale.Create(input);

        result.IsFailure.ShouldBeTrue();
        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(Locale.InvalidCode);
    }

    [Fact]
    public void EqualTags_AreEqualAfterCanonicalisation() => Locale.Parse("es_ar").ShouldBe(Locale.Parse("ES-AR"));

    [Fact]
    public void Parse_ThrowsOnInvalidTags() => Should.Throw<FormatException>(() => Locale.Parse("nope-"));
}
