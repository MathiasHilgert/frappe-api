using Frappe.Localization;
using Frappe.Notifications.Channels.Email;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Kinds.Marketing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    private static async Task<Exception?> StartAsync(EmailWorld world)
    {
        try
        {
            await world.Checks.OfType<EmailKindsCheck>().Single().StartAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return exception;
        }
    }

    [Test]
    public async Task A_kind_named_by_two_text_sources_stops_the_host_at_startup()
    {
        // Arrange
        var twin = new FakeTextSource();
        foreach (var language in Languages)
        {
            twin.With("product_announcement", language, ("subject", "otro"));
        }

        await using var world = new EmailWorld(services => services.AddSingleton<INotificationTextSource>(twin));

        // Act
        var failure = await StartAsync(world);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure!.Message).Contains("product_announcement");
        }
    }

    [Test]
    [Arguments("pt-BR", "", false, "missing in pt-BR")]
    [Arguments("en", "unsubscribe_label", true, "unsubscribe_label")]
    [Arguments("es", "unsubscribe_text", true, "unsubscribe_text")]
    public async Task The_texts_of_the_channel_itself_are_composed_in_every_language_at_startup(
        string broken, string key, bool written, string expected)
    {
        // Arrange
        var channel = new FakeTextSource();
        foreach (var language in Languages.Where(language => language != broken || written))
        {
            var keys = new[] { "unsubscribe_text", "unsubscribe_label" }.Where(candidate => language != broken || candidate != key);
            channel.With(EmailChannel.ChannelTexts, language, [.. keys.Select(candidate => (candidate, "texto"))]);
        }

        await using var world = new EmailWorld(services =>
        {
            services.RemoveAll<INotificationTextSource>();
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(AccountActivationCode).Assembly));
            services.AddSingleton<INotificationTextSource>(new EmbeddedNotificationTextSource(typeof(ProductAnnouncement).Assembly));
            services.AddSingleton<INotificationTextSource>(channel);
        });

        // Act
        var failure = await StartAsync(world);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.Message).Contains(expected);
        }
    }

    [Test]
    public async Task A_regional_spanish_falls_back_to_the_default_before_english()
    {
        // Arrange
        var source = new FakeTextSource()
            .With("k", "en", ("subject", "english"))
            .With("k", "es-AR", ("subject", "argentino"));
        var texts = new IcuNotificationTexts([source]);

        // Act
        var served = new[] { "es", "es-MX", "es-CL" }.Select(tag => texts.For("k", LanguageTag.Create(tag).Value).Language.Value).ToArray();

        // Assert
        await Assert.That(served).IsEquivalentTo(["es-AR", "es-AR", "es-AR"]);
    }
}
