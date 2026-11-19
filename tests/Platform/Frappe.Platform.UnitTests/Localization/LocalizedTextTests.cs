using Frappe.Localization;
using Frappe.Results;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>How localized text is built, picked for a language and compared.</summary>
public sealed class LocalizedTextTests
{
    private static LanguageTag Tag(string raw) => LanguageTag.Create(raw).Value;

    private static LocalizedText Text(params (string Language, string Text)[] translations) =>
        LocalizedText.Create(translations.Select(t => (Tag(t.Language), t.Text))).Value;

    [Test]
    public async Task Resolves_the_exact_language()
    {
        // Arrange
        var name = Text(("es", "Año nuevo"), ("en", "New Year's Day"));

        // Act
        var resolved = name.In(Tag("en")).Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(resolved.Text).IsEqualTo("New Year's Day");
            await Assert.That(resolved.Language.Value).IsEqualTo("en");
        }
    }

    [Test]
    public async Task A_regional_request_may_match_its_base_language()
    {
        // Arrange
        var name = Text(("es", "Año nuevo"));

        // Act
        var resolved = name.In(Tag("es-AR")).Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(resolved.Text).IsEqualTo("Año nuevo");
            await Assert.That(resolved.Language.Value).IsEqualTo("es");
        }
    }

    [Test]
    public async Task Never_falls_back_to_a_different_language()
    {
        // Arrange
        var name = Text(("es", "Año nuevo"));

        // Act
        var resolved = name.In(Tag("en"));

        // Assert
        var error = resolved.Errors[0];
        using (Assert.Multiple())
        {
            await Assert.That(resolved.IsSuccess).IsFalse();
            await Assert.That(error).IsTypeOf<RuleViolationError>();
            await Assert.That(error.Code).IsEqualTo("localization.translation.unavailable");
            await Assert.That(error.Parameters["requested"]).IsEqualTo("en");
            await Assert.That((string[])error.Parameters["available"]!).IsEquivalentTo(["es"]);
        }
    }

    [Test]
    public async Task A_base_language_request_does_not_match_a_regional_translation()
    {
        // Act
        var name = Text(("es-AR", "Año nuevo"));

        // Assert
        await Assert.That(name.In(Tag("es")).IsSuccess).IsFalse();
    }

    [Test]
    public async Task Requires_at_least_one_translation()
    {
        // Act
        var text = LocalizedText.Create([]);

        // Assert
        await Assert.That(text.Errors[0].Code).IsEqualTo("localization.text.empty");
    }

    [Test]
    public async Task Collects_every_invalid_translation()
    {
        // Act
        var text = LocalizedText.Create([(Tag("es"), " "), (Tag("es"), "Otro"), (Tag("en"), new string('x', 201))]);

        // Assert
        await Assert.That(text.Errors.Select(e => e.Code)).IsEquivalentTo(
            ["localization.translation.blank", "localization.translation.duplicate_language", "localization.translation.too_long"]);
    }

    [Test]
    public async Task Accepts_a_translation_of_exactly_the_maximum_length()
    {
        // Act
        var text = LocalizedText.Create([(Tag("en"), new string('x', Translation.MaxLength))]);

        // Assert
        await Assert.That(text.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Equality_ignores_the_order_of_translations()
    {
        // Arrange
        var first = Text(("es", "Año nuevo"), ("en", "New Year's Day"));

        // Act
        var second = Text(("en", "New Year's Day"), ("es", "Año nuevo"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(second);
            await Assert.That(first).IsNotEqualTo(Text(("es", "Año Nuevo")));
        }
    }

    [Test]
    public async Task Stored_text_without_translations_is_rejected()
    {
        // Act
        var act = () => LocalizedText.FromStored([]);

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Texts_with_the_same_translations_in_any_order_are_equal()
    {
        // Arrange
        var first = Text(("es", "Hola"), ("en", "Hello"));
        var second = Text(("en", "Hello"), ("es", "Hola"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Equals(second)).IsTrue();
            await Assert.That(first.GetHashCode()).IsEqualTo(second.GetHashCode());
        }
    }

    [Test]
    public async Task Texts_differing_in_one_translation_are_not_equal()
    {
        // Arrange
        var first = Text(("es", "Hola"), ("en", "Hello"));
        var second = Text(("es", "Hola"), ("en", "Hi"));

        // Assert
        await Assert.That(first.Equals(second)).IsFalse();
    }
}
