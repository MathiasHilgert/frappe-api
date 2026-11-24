using Frappe.Localization;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Kinds.Marketing;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>The embedded ICU texts: every language has every key another has (S6, S8).</summary>
public sealed class KindTextsTests
{
    private static readonly string[] Languages = ["es-AR", "es", "en", "pt-BR"];

    public static IEnumerable<Func<EmbeddedNotificationTextSource>> Sources() =>
    [
        () => new EmbeddedNotificationTextSource(typeof(EmailChannel).Assembly),
        () => new EmbeddedNotificationTextSource(typeof(AccountActivationCode).Assembly),
        () => new EmbeddedNotificationTextSource(typeof(ProductAnnouncement).Assembly),
    ];

    [Test]
    [MethodDataSource(nameof(Sources))]
    public async Task Every_plug_in_has_exactly_the_four_languages(EmbeddedNotificationTextSource source)
    {
        // Act
        var languages = source.Languages.Select(language => language.Value);

        // Assert
        await Assert.That(languages).IsEquivalentTo(Languages);
    }

    [Test]
    [MethodDataSource(nameof(Sources))]
    public async Task Every_language_has_the_kinds_and_keys_of_every_other(EmbeddedNotificationTextSource source)
    {
        // Arrange
        var texts = source;
        var spanish = LanguageTag.Create("es-AR").Value;
        var kinds = texts.KindsOf(spanish);

        // Act
        var differences = new List<string>();
        foreach (var language in texts.Languages)
        {
            var kindsOfLanguage = texts.KindsOf(language);
            differences.AddRange(kinds.Except(kindsOfLanguage).Select(kind => $"{language}: missing kind {kind}"));
            differences.AddRange(kindsOfLanguage.Except(kinds).Select(kind => $"{language}: extra kind {kind}"));
            foreach (var kind in kinds.Intersect(kindsOfLanguage))
            {
                var expected = texts.Find(kind, spanish)!.Keys;
                var actual = texts.Find(kind, language)!.Keys;
                differences.AddRange(expected.Except(actual).Select(key => $"{language}: {kind} misses {key}"));
                differences.AddRange(actual.Except(expected).Select(key => $"{language}: {kind} has extra {key}"));
            }
        }

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(kinds).IsNotEmpty();
            await Assert.That(differences).IsEmpty();
        }
    }

    [Test]
    public async Task Every_registered_kind_has_texts_in_every_language()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var missing = world.Catalog.Kinds.SelectMany(kind => NotificationLanguages.All
            .Where(language => world.Composer.CanCompose(kind) && world.Composer.Compose(kind, kind.Sample, language).Language != language)
            .Select(language => $"{kind.Name} {language}")).ToList();

        // Assert
        await Assert.That(missing).IsEmpty();
    }
}
