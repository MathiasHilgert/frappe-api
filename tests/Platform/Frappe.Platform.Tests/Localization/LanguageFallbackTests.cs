using Frappe.Localization;

namespace Frappe.Platform.Tests.Localization;

public sealed class LanguageFallbackTests
{
    [Test]
    [Arguments("es-AR", "es-AR,es,en")]
    [Arguments("es-MX", "es-MX,es,en")]
    [Arguments("es-CL", "es-CL,es,en")]
    [Arguments("es", "es,en")]
    [Arguments("pt-BR", "pt-BR,pt,en")]
    [Arguments("pt-PT", "pt-PT,pt,pt-BR,en")]
    [Arguments("pt", "pt,pt-BR,en")]
    [Arguments("en-GB", "en-GB,en")]
    [Arguments("de-AT", "de-AT,de,en")]
    public async Task Requests_narrow_to_their_language_then_its_preferred_variant_then_English(string requested, string expected)
    {
        var chain = LanguageFallback.For(LanguageTag.Create(requested).Value);

        await Assert.That(string.Join(",", chain.Select(tag => tag.Value))).IsEqualTo(expected);
    }

    [Test]
    public async Task The_supported_languages_are_English_Spanish_with_Argentina_and_Mexico_and_Brazilian_Portuguese()
    {
        await Assert.That(LanguageFallback.Supported.Select(tag => tag.Value).ToArray())
            .IsEquivalentTo(["en", "es", "es-AR", "es-MX", "pt-BR"]);
    }
}
