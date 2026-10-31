using Frappe.Platform.Http.Localization;

namespace Frappe.Platform.UnitTests.Http;

public sealed class ErrorLocaleTests
{
    [Theory]
    [InlineData("es", "es")]
    [InlineData("EN", "en")]
    [InlineData("fil", "fil")]
    [InlineData("es-AR", "es-AR")]
    [InlineData("es_ar", "es-AR")]
    [InlineData(" pt-br ", "pt-BR")]
    [InlineData("zh-hant-tw", "zh-Hant-TW")]
    [InlineData("SR-LATN", "sr-Latn")]
    [InlineData("es-419", "es-419")]
    [InlineData("de-CH-1996", "de-CH-1996")]
    [InlineData("sl-ROZAJ", "sl-rozaj")]
    [InlineData("ca-ES-VALENCIA", "ca-ES-valencia")]
    public void A_BCP_47_tag_is_accepted_in_canonical_case(string raw, string expected)
    {
        var locale = ErrorLocale.Parse(raw);

        locale.ShouldNotBeNull();
        locale.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("e")]
    [InlineData("english")]
    [InlineData("es-")]
    [InlineData("es--AR")]
    [InlineData("es AR")]
    [InlineData("es-ARG")]
    [InlineData("en-12")]
    [InlineData("en-US-Latn")]
    [InlineData("zh-cmn")]
    [InlineData("en-a1b2c3d4e")]
    [InlineData("en-US-u-ca-gregory")]
    [InlineData("en-x-private")]
    [InlineData("x-private")]
    [InlineData("ñu")]
    [InlineData("de-DE-1901-1996-abcde-fghij-klmno-pqrst")]
    public void A_value_outside_the_supported_BCP_47_subset_is_rejected(string? raw)
    {
        var locale = ErrorLocale.Parse(raw);

        locale.ShouldBeNull();
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("zh-Hant-TW", "zh")]
    [InlineData("es", "es")]
    public void The_language_is_the_primary_subtag(string raw, string expected)
    {
        ErrorLocale locale = ErrorLocale.Parse(raw)!;

        ErrorLocale language = locale.Language;

        language.Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("es-AR", new[] { "es-AR", "es", "en" })]
    [InlineData("zh-Hant-TW", new[] { "zh-Hant-TW", "zh", "en" })]
    [InlineData("es", new[] { "es", "en" })]
    [InlineData("en-GB", new[] { "en-GB", "en" })]
    [InlineData("en", new[] { "en" })]
    public void The_fallback_chain_is_the_exact_tag_then_its_language_then_English(
        string raw,
        string[] expected
    )
    {
        ErrorLocale locale = ErrorLocale.Parse(raw)!;

        IReadOnlyList<ErrorLocale> chain = locale.FallbackChain();

        chain.Select(static l => l.Value).ShouldBe(expected);
    }

    [Fact]
    public void English_is_the_en_tag()
    {
        ErrorLocale english = ErrorLocale.English;

        english.ShouldBe(ErrorLocale.Parse("en"));
    }

    [Fact]
    public void Tags_differing_only_in_case_and_separator_are_equal()
    {
        ErrorLocale loose = ErrorLocale.Parse("es_ar")!;

        ErrorLocale canonical = ErrorLocale.Parse("es-AR")!;

        loose.ShouldBe(canonical);
    }

    [Fact]
    public void The_text_form_is_the_tag()
    {
        ErrorLocale locale = ErrorLocale.Parse("zh_hant_tw")!;

        string text = locale.ToString();

        text.ShouldBe("zh-Hant-TW");
    }
}
