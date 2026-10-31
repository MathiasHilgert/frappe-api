using ErrorOr;
using Frappe.Kernel.Geography;
using Frappe.Kernel.Localization;
using Frappe.Platform.Http;
using Frappe.Platform.Http.Localization;

namespace Frappe.Platform.UnitTests.Http;

public sealed class ErrorTextsTests
{
    private static readonly ErrorTexts _texts = new([typeof(HttpExtensions).Assembly]);

    [Fact]
    public void A_regional_locale_falls_back_to_its_language()
    {
        string text = _texts.Render(CountryCode.Invalid, Locale.Create("es-AR").Value);

        text.ShouldBe("El código de país debe ser de dos letras ISO 3166-1 alfa-2.");
    }

    [Fact]
    public void A_locale_without_texts_falls_back_to_english()
    {
        string text = _texts.Render(CountryCode.Invalid, Locale.Create("fr").Value);

        text.ShouldBe("The country code must be two ISO 3166-1 alpha-2 letters.");
    }

    [Fact]
    public void A_code_without_texts_falls_back_to_its_description()
    {
        var error = Error.Validation("test.thing.invalid", "The thing is invalid.");

        string text = _texts.Render(error, Locale.Create("pt").Value);

        text.ShouldBe("The thing is invalid.");
    }

    [Fact]
    public void Arguments_replace_their_placeholders()
    {
        var error = Error.Validation("test.thing.too_long", "At most {max} characters.");

        string text = _texts.Render(
            error,
            Locale.English,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["max"] = 100 }
        );

        text.ShouldBe("At most 100 characters.");
    }
}
