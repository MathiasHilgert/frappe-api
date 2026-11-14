using Frappe.Localization;
using Frappe.Results;

namespace Frappe.Platform.Tests.Localization;

public sealed class LocalizedTextTests
{
    private static LanguageTag Tag(string raw) => LanguageTag.Create(raw).Value;

    private static LocalizedText Text(params (string Language, string Text)[] translations) =>
        LocalizedText.Create(translations.Select(t => (Tag(t.Language), t.Text))).Value;

    [Test]
    public async Task Resolves_the_exact_language()
    {
        var name = Text(("es", "Año nuevo"), ("en", "New Year's Day"));

        var resolved = name.In(Tag("en")).Value;

        await Assert.That(resolved.Text).IsEqualTo("New Year's Day");
        await Assert.That(resolved.Language.Value).IsEqualTo("en");
    }

    [Test]
    public async Task A_regional_request_may_match_its_base_language()
    {
        var name = Text(("es", "Año nuevo"));

        var resolved = name.In(Tag("es-AR")).Value;

        await Assert.That(resolved.Text).IsEqualTo("Año nuevo");
        await Assert.That(resolved.Language.Value).IsEqualTo("es");
    }

    [Test]
    public async Task Never_falls_back_to_a_different_language()
    {
        var name = Text(("es", "Año nuevo"));

        var resolved = name.In(Tag("en"));

        await Assert.That(resolved.IsSuccess).IsFalse();
        var error = resolved.Errors[0];
        await Assert.That(error).IsTypeOf<RuleViolationError>();
        await Assert.That(error.Code).IsEqualTo("localization.translation.unavailable");
        await Assert.That(error.Parameters["requested"]).IsEqualTo("en");
        await Assert.That((string[])error.Parameters["available"]!).IsEquivalentTo(["es"]);
    }

    [Test]
    public async Task A_base_language_request_does_not_match_a_regional_translation()
    {
        var name = Text(("es-AR", "Año nuevo"));

        await Assert.That(name.In(Tag("es")).IsSuccess).IsFalse();
    }

    [Test]
    public async Task Requires_at_least_one_translation()
    {
        var text = LocalizedText.Create([]);

        await Assert.That(text.Errors[0].Code).IsEqualTo("localization.text.empty");
    }

    [Test]
    public async Task Collects_every_invalid_translation()
    {
        var text = LocalizedText.Create([(Tag("es"), " "), (Tag("es"), "Otro"), (Tag("en"), new string('x', 201))]);

        await Assert.That(text.Errors.Select(e => e.Code)).IsEquivalentTo(
            ["localization.translation.blank", "localization.translation.duplicate_language", "localization.translation.too_long"]);
    }

    [Test]
    public async Task Equality_ignores_the_order_of_translations()
    {
        var first = Text(("es", "Año nuevo"), ("en", "New Year's Day"));
        var second = Text(("en", "New Year's Day"), ("es", "Año nuevo"));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).IsNotEqualTo(Text(("es", "Año Nuevo")));
    }
}
