using Frappe.Localization;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>Texts rebuilt from storage, their equality, the served languages and the tag's text form.</summary>
public sealed class LocalizedTextStoredTests
{
    private static LanguageTag Tag(string raw) => LanguageTag.Create(raw).Value;

    [Test]
    public async Task A_stored_text_exposes_its_translations_and_equals_the_same_text_in_any_order()
    {
        // Arrange
        var es = new Translation(Tag("es"), "Año nuevo");
        var en = new Translation(Tag("en"), "New Year's Day");

        // Act
        var stored = LocalizedText.FromStored([es, en]);
        var reordered = LocalizedText.FromStored([en, es]);
        var different = LocalizedText.FromStored([es]);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stored.Translations).Count().IsEqualTo(2);
            await Assert.That(stored.Equals(reordered)).IsTrue();
            await Assert.That(stored.Equals((object)reordered)).IsTrue();
            await Assert.That(stored.GetHashCode()).IsEqualTo(reordered.GetHashCode());
            await Assert.That(stored.Equals(different)).IsFalse();
            await Assert.That(stored.Equals((object)different)).IsFalse();
        }
    }

    [Test]
    public async Task A_stored_text_without_translations_is_rejected()
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => LocalizedText.FromStored([]));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task A_language_is_served_when_it_narrows_to_a_supported_one_and_English_is_no_last_resort()
    {
        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(LanguageFallback.IsServed(Tag("es-CL"))).IsTrue();
            await Assert.That(LanguageFallback.IsServed(Tag("pt-PT"))).IsTrue();
            await Assert.That(LanguageFallback.IsServed(Tag("fr"))).IsFalse();
        }
    }

    [Test]
    public async Task A_language_tag_prints_its_value()
    {
        // Act
        var text = Tag("es-AR").ToString();

        // Assert
        await Assert.That(text).IsEqualTo(Tag("es-AR").Value);
    }
}
