using Frappe.Localization;
using Frappe.Results;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>How a language tag is validated, normalized and compared.</summary>
public sealed class LanguageTagTests
{
    [Test]
    [Arguments("es", "es")]
    [Arguments("ES-ar", "es-AR")]
    [Arguments("zh-hant-tw", "zh-Hant-TW")]
    [Arguments("es-419", "es-419")]
    public async Task Normalizes_the_case_of_each_subtag(string raw, string expected)
    {
        // Act
        var tag = LanguageTag.Create(raw);

        // Assert
        await Assert.That(tag.Value.Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("e")]
    [Arguments("spanish")]
    [Arguments("es_AR")]
    [Arguments("es-AR-")]
    [Arguments("*")]
    public async Task Rejects_anything_that_is_not_a_single_language_tag(string raw)
    {
        // Act
        var tag = LanguageTag.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tag.IsSuccess).IsFalse();
            await Assert.That(tag.Errors[0]).IsTypeOf<InvalidValueError>();
            await Assert.That(tag.Errors[0].Code).IsEqualTo("localization.language_tag.invalid_format");
        }
    }

    [Test]
    public async Task Lookup_chain_drops_subtags_from_the_right_as_RFC_4647_describes()
    {
        // Act
        var tag = LanguageTag.Create("zh-Hant-TW").Value;

        // Assert
        await Assert.That(tag.LookupChain().Select(t => t.Value)).IsEquivalentTo(["zh-Hant-TW", "zh-Hant", "zh"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
