using System.Globalization;
using Frappe.Localization;

namespace Frappe.Platform.UnitTests.Localization;

/// <summary>The one ICU MessageFormat wrapper the error messages and the mail texts share.</summary>
public sealed class IcuMessagesTests
{
    private static LanguageTag Tag(string value) => LanguageTag.Create(value).Value;

    [Test]
    [Arguments("en", 1, "1 error was found.")]
    [Arguments("en", 3, "3 errors were found.")]
    public async Task A_pattern_is_formatted_with_the_plural_rules_of_its_language(string language, int count, string expected)
    {
        // Act
        var text = IcuMessages.Format(
            "{count, plural, one {# error was found.} other {# errors were found.}}",
            new Dictionary<string, object?> { ["count"] = count },
            Tag(language));

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    public async Task Arguments_are_replaced_by_name()
    {
        // Act
        var text = IcuMessages.Format(
            "Hola {name}. Tu código: {code}", new Dictionary<string, object?> { ["name"] = "Martina", ["code"] = "482913" }, Tag("es-AR"));

        // Assert
        await Assert.That(text).IsEqualTo("Hola Martina. Tu código: 482913");
    }

    [Test]
    public async Task The_culture_of_a_language_is_its_own()
    {
        // Act
        var portuguese = IcuMessages.CultureOf(Tag("pt-BR")).Name;
        var argentinian = IcuMessages.CultureOf(Tag("es-AR")).Name;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(portuguese).IsEqualTo("pt-BR");
            await Assert.That(argentinian).IsEqualTo("es-AR");
        }
    }

    [Test]
    public async Task A_culture_that_cannot_be_found_formats_in_the_invariant_culture_instead_of_failing()
    {
        // Act
        var culture = IcuMessages.CultureOf("es-AR", name => throw new CultureNotFoundException(nameof(name), name, "invariant mode"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(culture).IsEqualTo(CultureInfo.InvariantCulture);
            await Assert.That(IcuMessages.CultureOf("pt-BR", CultureInfo.GetCultureInfo).Name).IsEqualTo("pt-BR");
        }
    }

    [Test]
    public async Task A_malformed_pattern_or_a_missing_argument_throws()
    {
        // Act
        var malformed = Caught.ExceptionOf(
            () => IcuMessages.Format("{count, plural, one {x}", new Dictionary<string, object?> { ["count"] = 1 }, Tag("en")));
        var missing = Caught.ExceptionOf(() => IcuMessages.Format("Hello {name}", new Dictionary<string, object?>(), Tag("en")));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(malformed).IsNotNull();
            await Assert.That(missing).IsNotNull();
        }
    }
}
