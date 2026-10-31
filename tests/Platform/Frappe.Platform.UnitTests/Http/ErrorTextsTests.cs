using System.Text;
using Frappe.Platform.Http;
using Frappe.Platform.Http.Localization;
using Frappe.Platform.Http.Problems;

namespace Frappe.Platform.UnitTests.Http;

public sealed class ErrorTextsTests
{
    private static readonly ErrorTexts _texts = new([typeof(HttpExtensions).Assembly]);

    [Fact]
    public void A_regional_locale_falls_back_to_its_language()
    {
        string? text = _texts.Render(
            PlatformErrors.InvalidCursor.Code,
            ErrorLocale.Parse("es-AR")!
        );

        text.ShouldBe("El cursor no es válido; volvé a empezar desde la primera página.");
    }

    [Fact]
    public void A_locale_without_texts_falls_back_to_english()
    {
        string? text = _texts.Render(PlatformErrors.InvalidCursor.Code, ErrorLocale.Parse("fr")!);

        text.ShouldBe("The cursor is invalid; start again from the first page.");
    }

    // The caller decides what replaces a missing text; the description is never a silent fallback.
    [Fact]
    public void A_code_without_texts_has_no_text()
    {
        _texts.Render("test.thing.invalid", ErrorLocale.Parse("pt")!).ShouldBeNull();
    }

    [Fact]
    public void Arguments_replace_their_placeholders()
    {
        ErrorTexts texts = new([
            Resource("errors.en.json", "{\"test.thing.too_long\":\"At most {max}.\"}"),
        ]);

        string? text = texts.Render(
            "test.thing.too_long",
            ErrorLocale.English,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["max"] = 100 }
        );

        text.ShouldBe("At most 100.");
    }

    [Fact]
    public void A_resource_locale_is_canonicalized()
    {
        ErrorTexts texts = new([
            Resource("errors.pt-br.json", "{\"test.thing.invalid\":\"Inválido.\"}"),
        ]);

        texts.Translates("test.thing.invalid", "pt-BR").ShouldBeTrue();
    }

    [Theory]
    [InlineData("errors.json", "{}", "must be named errors.<locale>.json")]
    [InlineData("errors.not_a_locale!.json", "{}", "must be named errors.<locale>.json")]
    [InlineData("errors.es.json", "{\"test.thing.invalid\":", "must be a JSON object")]
    [InlineData("errors.es.json", "{\"test.thing.invalid\":5}", "must be a JSON object")]
    public void A_broken_resource_fails_the_load_naming_it(string name, string json, string reason)
    {
        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            new ErrorTexts([Resource(name, json)])
        );

        error.Message.ShouldContain(name);
        error.Message.ShouldContain(reason);
    }

    private static (string Name, Func<Stream> Open) Resource(string name, string json) =>
        (name, () => new MemoryStream(Encoding.UTF8.GetBytes(json)));
}
