using Frappe.Kernel.Localization;

namespace Frappe.Kernel.UnitTests.Localization;

public sealed class LocaleTests
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
        ErrorOr<Locale> result = Locale.Create(raw);

        result.Value.Value.ShouldBe(expected);
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
        ErrorOr<Locale> result = Locale.Create(raw);

        result.FirstError.Code.ShouldBe("kernel.locale.invalid");
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("zh-Hant-TW", "zh")]
    [InlineData("es", "es")]
    public void The_language_is_the_primary_subtag(string raw, string expected)
    {
        Locale locale = Locale.Create(raw).Value;

        Locale language = locale.Language;

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
        Locale locale = Locale.Create(raw).Value;

        IReadOnlyList<Locale> chain = locale.FallbackChain();

        chain.Select(static l => l.Value).ShouldBe(expected);
    }

    [Fact]
    public void English_is_the_en_tag()
    {
        Locale english = Locale.English;

        english.ShouldBe(Locale.Create("en").Value);
    }

    [Fact]
    public void Tags_differing_only_in_case_and_separator_are_equal()
    {
        Locale loose = Locale.Create("es_ar").Value;

        Locale canonical = Locale.Create("es-AR").Value;

        loose.ShouldBe(canonical);
    }

    [Fact]
    public void The_text_form_is_the_tag()
    {
        Locale locale = Locale.Create("zh_hant_tw").Value;

        string text = locale.ToString();

        text.ShouldBe("zh-Hant-TW");
    }
}
