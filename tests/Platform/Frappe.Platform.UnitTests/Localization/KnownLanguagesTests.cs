using Frappe.Localization;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>The closed list of languages a business may be written in: the languages .NET knows, committed so every host answers the same.</summary>
public sealed class KnownLanguagesTests
{
    [Test]
    [Arguments("es")]
    [Arguments("es-AR")]
    [Arguments("es-419")]
    [Arguments("zh-Hant-TW")]
    [Arguments("fr")]
    [Arguments("ja-JP")]
    [Arguments("zh-CN")]
    [Arguments("zh-TW")]
    [Arguments("sr-RS")]
    [Arguments("az-AZ")]
    public async Task A_language_in_its_canonical_spelling_is_known(string raw)
    {
        // Arrange
        var tag = LanguageTag.Create(raw).Value;

        // Act
        var known = KnownLanguages.Contains(tag);

        // Assert
        await Assert.That(known).IsTrue();
    }

    [Test]
    [Arguments("xx")]
    [Arguments("es-ZZ")]
    [Arguments("qq-QQ")]
    [Arguments("zzz")]
    public async Task A_well_formed_tag_nobody_defined_is_not_known(string raw)
    {
        // Arrange
        var tag = LanguageTag.Create(raw).Value;

        // Act
        var known = KnownLanguages.Contains(tag);

        // Assert
        await Assert.That(known).IsFalse();
    }

    [Test]
    public async Task Every_listed_language_is_a_canonical_tag_without_repetitions_in_ordinal_order()
    {
        // Act
        var all = KnownLanguages.All;

        // Assert
        var values = all.Select(tag => tag.Value).ToList();
        using (Assert.Multiple())
        {
            await Assert.That(values).IsEquivalentTo([.. values.Distinct(StringComparer.Ordinal)]);
            await Assert.That(values.SequenceEqual(values.OrderBy(value => value, StringComparer.Ordinal))).IsTrue();
            await Assert.That(values.All(value => LanguageTag.Create(value).Value.Value == value)).IsTrue();
            await Assert.That(values.All(value => value.Length <= LanguageTag.StorageLength)).IsTrue();
        }
    }

    [Test]
    public async Task Every_language_Frappe_is_written_in_is_known()
    {
        // Act
        var missing = LanguageFallback.Supported.Where(tag => !KnownLanguages.Contains(tag)).ToList();

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task The_list_has_the_size_the_committed_generation_produced()
    {
        // Act
        var count = KnownLanguages.All.Count;

        // Assert
        await Assert.That(count).IsEqualTo(937);
    }
}
