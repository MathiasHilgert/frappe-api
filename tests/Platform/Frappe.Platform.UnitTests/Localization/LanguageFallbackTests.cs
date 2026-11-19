using Frappe.Localization;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>The fallback chain of each supported language and the list of supported languages.</summary>
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
        // Act
        var chain = LanguageFallback.For(LanguageTag.Create(requested).Value);

        // Assert
        await Assert.That(string.Join(",", chain.Select(tag => tag.Value))).IsEqualTo(expected);
    }

    [Test]
    public async Task The_supported_languages_are_English_Spanish_with_Argentina_and_Mexico_and_Brazilian_Portuguese()
    {
        // Act
        var supported = LanguageFallback.Supported.Select(tag => tag.Value).ToArray();

        // Assert
        await Assert.That(supported).IsEquivalentTo(["en", "es", "es-AR", "es-MX", "pt-BR"]);
    }

    [Test]
    [Arguments("es-CL", true)]
    [Arguments("pt-PT", true)]
    [Arguments("fr", false)]
    [Arguments("en", true)]
    public async Task A_language_is_served_when_it_narrows_to_a_supported_one_without_falling_back_to_English(string requested, bool expected)
    {
        // Act
        var served = LanguageFallback.IsServed(LanguageTag.Create(requested).Value);

        // Assert
        await Assert.That(served).IsEqualTo(expected);
    }
}
