using Frappe.Localization;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>Which translation is picked for a requested language, following the fallback chain.</summary>
public sealed class TranslationPickTests
{
    /// <summary>A text in one language, the sample translation.</summary>
    private sealed record Name(LanguageTag Language, string Text);

    private static LanguageTag Tag(string value) => LanguageTag.Create(value).Value;

    private static readonly Name[] Names =
        [new(Tag("en"), "Argentine Peso"), new(Tag("es"), "peso argentino"), new(Tag("es-AR"), "peso"), new(Tag("pt-BR"), "Peso argentino")];

    [Test]
    [Arguments("es-AR", "peso")]
    [Arguments("es-CL", "peso argentino")]
    [Arguments("pt-PT", "Peso argentino")]
    [Arguments("fr", "Argentine Peso")]
    public async Task The_platform_fallback_picks_the_translation(string requested, string expected)
    {
        // Act
        var picked = LanguageFallback.Pick(Names, n => n.Language, Tag(requested));

        // Assert
        await Assert.That(picked!.Text).IsEqualTo(expected);
    }

    [Test]
    public async Task Data_in_one_other_language_is_still_served_and_nothing_is_null()
    {
        // Arrange
        Name[] onlyGerman = [new(Tag("de"), "Peso")];

        // Act
        var served = LanguageFallback.Pick(onlyGerman, n => n.Language, Tag("es"));
        var none = LanguageFallback.Pick(Array.Empty<Name>(), n => n.Language, Tag("es"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(served!.Text).IsEqualTo("Peso");
            await Assert.That(none).IsNull();
        }
    }
}
