using Frappe.Localization;
using Frappe.Notifications.Senders.Email;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>ICU texts and the language they are served in (S6, S8).</summary>
public sealed class NotificationTextsTests
{
    private static LanguageTag Tag(string value) => LanguageTag.Create(value).Value;

    private static IcuNotificationTexts Texts() => new([
        new FakeTextSource()
            .With("kind", "es-AR", ("hello", "Hola, {Name}"))
            .With("kind", "es", ("hello", "Hola {Name} (es)"))
            .With("kind", "en", ("hello", "Hello, {Name}"))
            .With("kind", "pt-BR", ("hello", "Olá, {Name}")),
    ]);

    [Test]
    public async Task A_text_is_formatted_with_the_properties_of_the_arguments()
    {
        // Act
        var text = Texts().For("kind", Tag("en"))["hello", new { Name = "Ana" }];

        // Assert
        await Assert.That(text).IsEqualTo("Hello, Ana");
    }

    [Test]
    public async Task A_missing_key_fails()
    {
        // Act
        var thrown = Assert.Throws<KeyNotFoundException>(() => _ = Texts().For("kind", Tag("en"))["nope"]);

        // Assert
        await Assert.That(thrown.Message).Contains("nope");
    }

    [Test]
    public async Task A_missing_argument_fails()
    {
        // Act
        var thrown = Assert.Throws<Jeffijoe.MessageFormat.MessageFormatterException>(
            () => _ = Texts().For("kind", Tag("en"))["hello", new { Other = 1 }]);

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    [Arguments("es-AR", "es-AR")]
    [Arguments("es-MX", "es")]
    [Arguments("es", "es")]
    [Arguments("en-GB", "en")]
    [Arguments("pt-PT", "pt-BR")]
    [Arguments("pt-BR", "pt-BR")]
    [Arguments("fr", "es-AR")]
    [Arguments("ja-JP", "es-AR")]
    public async Task The_language_served_follows_the_fallback_and_ends_in_the_default(string requested, string served)
    {
        // Act
        var texts = Texts().For("kind", Tag(requested));

        // Assert
        await Assert.That(texts.Language.Value).IsEqualTo(served);
    }

    [Test]
    public async Task No_language_gets_the_default()
    {
        // Act
        var texts = Texts().For("kind", null);

        // Assert
        await Assert.That(texts.Language.Value).IsEqualTo("es-AR");
    }

    [Test]
    public async Task A_language_the_kind_lacks_falls_back_down_the_chain()
    {
        // Arrange
        var texts = new IcuNotificationTexts([
            new FakeTextSource().With("kind", "es-AR", ("hello", "Hola")).With("kind", "es", ("hello", "Hola es")),
        ]);

        // Act
        var served = texts.For("kind", Tag("pt-BR"));

        // Assert
        await Assert.That(served.Language.Value).IsEqualTo("es-AR");
    }

    [Test]
    public async Task A_kind_without_texts_fails()
    {
        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => Texts().For("other", Tag("en")));

        // Assert
        await Assert.That(thrown.Message).Contains("other");
    }

    [Test]
    public async Task The_languages_of_the_texts_are_the_four_of_the_spec()
    {
        // Assert
        await Assert.That(NotificationLanguages.All.Select(language => language.Value)).IsEquivalentTo(["es-AR", "es", "en", "pt-BR"]);
    }
}
