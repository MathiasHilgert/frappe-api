using Frappe.Localization;

namespace Frappe.Platform.Tests.Localization;

public sealed class TranslationPickTests
{
    private sealed record Name(LanguageTag Language, string Text);

    private static LanguageTag Tag(string value) => LanguageTag.Create(value).Value;

    private static readonly Name[] Names = [new(Tag("en"), "Argentine Peso"), new(Tag("es"), "peso argentino"), new(Tag("es-AR"), "peso"), new(Tag("pt-BR"), "Peso argentino")];

    [Test]
    [Arguments("es-AR", "peso")]
    [Arguments("es-CL", "peso argentino")]
    [Arguments("pt-PT", "Peso argentino")]
    [Arguments("fr", "Argentine Peso")]
    public async Task The_platform_fallback_picks_the_translation(string requested, string expected) =>
        await Assert.That(LanguageFallback.Pick(Names, n => n.Language, Tag(requested))!.Text).IsEqualTo(expected);

    [Test]
    public async Task Data_in_one_other_language_is_still_served_and_nothing_is_null()
    {
        Name[] onlyGerman = [new(Tag("de"), "Peso")];

        await Assert.That(LanguageFallback.Pick(onlyGerman, n => n.Language, Tag("es"))!.Text).IsEqualTo("Peso");
        await Assert.That(LanguageFallback.Pick(Array.Empty<Name>(), n => n.Language, Tag("es"))).IsNull();
    }
}
