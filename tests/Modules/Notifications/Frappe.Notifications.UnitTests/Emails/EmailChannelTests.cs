using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.UnitTests.Dispatching;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>What the email channel does with a delivery, and what the startup check refuses (S7, S8, S9).</summary>
public sealed class EmailChannelTests
{
    private static readonly string[] Languages = ["es-AR", "es", "en", "pt-BR"];

    private static FakeTextSource TextsOf(string kind, params (string Key, string Pattern)[] patterns) =>
        Languages.Aggregate(new FakeTextSource(), (source, language) => source.With(kind, language, patterns));

    private static Action<IServiceCollection> WithKind<TNotification>(FakeTextSource texts)
        where TNotification : INotification<TestPayload>, new() => services => services
            .AddSingleton<INotificationTextSource>(texts)
            .AddSingleton(NotificationKind.Of<TNotification, TestPayload>());

    private static async Task<Exception?> StartAsync(EmailWorld world)
    {
        try
        {
            await world.Checks.OfType<EmailKindsCheck>().Single().StartAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or KeyNotFoundException or Jeffijoe.MessageFormat.MessageFormatterException)
        {
            return exception;
        }
    }

    [Test]
    public async Task The_channel_is_called_email_and_delivers_only_kinds_with_an_email()
    {
        // Arrange
        await using var world = new EmailWorld(services => services.AddSingleton(NotificationKind.Of<TestSecurityNotification, TestPayload>()));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(world.Channel.Name.Value).IsEqualTo("email");
            await Assert.That(world.Channel.CanDeliver(world.Kind("account_activation_code"))).IsTrue();
            await Assert.That(world.Channel.CanDeliver(world.Kind("product_announcement"))).IsTrue();
            await Assert.That(world.Channel.CanDeliver(world.Kind("test_security"))).IsFalse();
        }
    }

    [Test]
    public async Task The_sent_outcome_names_the_language_served()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("account_activation_code", "pt-PT"), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsTypeOf<ChannelOutcome.Sent>();
            await Assert.That(((ChannelOutcome.Sent)outcome).LocaleServed!.Value).IsEqualTo("pt-BR");
            await Assert.That(((ChannelOutcome.Sent)outcome).ProviderMessageId).IsEqualTo("msg_1");
            await Assert.That(world.Sender.Sent[0].Language.Value).IsEqualTo("pt-BR");
        }
    }

    [Test]
    [Arguments("fr")]
    [Arguments("ja-JP")]
    public async Task A_language_that_is_not_ours_is_served_in_the_default(string language)
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("account_activation_code", language), CancellationToken.None);

        // Assert
        await Assert.That(((ChannelOutcome.Sent)outcome).LocaleServed!.Value).IsEqualTo("es-AR");
    }

    [Test]
    public async Task A_recipient_without_a_language_is_served_in_the_default()
    {
        // Arrange
        await using var world = new EmailWorld();
        var payload = AccountActivationCode.Sample with { Language = null };

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("account_activation_code", payload: payload), CancellationToken.None);

        // Assert
        await Assert.That(((ChannelOutcome.Sent)outcome).LocaleServed!.Value).IsEqualTo("es-AR");
    }

    [Test]
    public async Task An_address_that_is_not_an_address_fails_for_good_and_sends_nothing()
    {
        // Arrange
        await using var world = new EmailWorld();
        var payload = AccountActivationCode.Sample with { Email = "no-es-un-mail" };

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("account_activation_code", payload: payload), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.InvalidRecipient));
            await Assert.That(world.Sender.Sent).IsEmpty();
        }
    }

    [Test]
    public async Task The_address_is_normalized_before_it_is_sent()
    {
        // Arrange
        await using var world = new EmailWorld();
        var payload = AccountActivationCode.Sample with { Email = "  Ana@Example.COM " };

        // Act
        await world.Channel.DeliverAsync(world.ItemOf("account_activation_code", payload: payload), CancellationToken.None);

        // Assert
        await Assert.That(world.Sender.Sent[0].To.Value).IsEqualTo("ana@example.com");
    }

    [Test]
    public async Task The_sender_outcome_that_is_not_sent_passes_through()
    {
        // Arrange
        await using var world = new EmailWorld();
        world.Sender.Outcome = new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected);

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("account_activation_code"), CancellationToken.None);

        // Assert
        await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected));
    }

    [Test]
    public async Task The_kinds_have_the_categories_of_the_spec()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(world.Catalog.Kinds.Select(kind => (kind.Name, kind.Category))).IsEquivalentTo(
            [
                ("account_activation_code", NotificationCategory.Security),
                ("password_reset_code", NotificationCategory.Security),
                ("email_change_code", NotificationCategory.Security),
                ("registration_attempted", NotificationCategory.Security),
                ("email_change_requested", NotificationCategory.Security),
                ("email_changed", NotificationCategory.Transactional),
                ("product_announcement", NotificationCategory.Marketing),
            ]);
        }
    }

    [Test]
    public async Task A_kind_payload_names_an_explicit_email_contact_and_prints_no_secret()
    {
        // Arrange
        var payload = new AccountActivationCodePayload("ana@example.com", "es-AR", "Ana", "482913");
        var announcement = ProductAnnouncement.Sample;

        // Act
        var recipient = new AccountActivationCode().RecipientOf(payload);
        var printed = $"{payload} {announcement} {new ProductAnnouncement().RecipientOf(announcement)}";

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(recipient).IsTypeOf<ContactRecipient>();
            await Assert.That(((ContactRecipient)recipient).Channel.Value).IsEqualTo("email");
            await Assert.That(((ContactRecipient)recipient).Language!.Value).IsEqualTo("es-AR");
            await Assert.That(printed).DoesNotContain("482913");
            await Assert.That(printed).DoesNotContain("ana@example.com");
            await Assert.That(printed).DoesNotContain("Ana");
        }
    }

    [Test]
    public async Task The_startup_check_passes_for_the_four_kinds()
    {
        // Arrange
        await using var world = new EmailWorld();

        // Act
        var failure = await StartAsync(world);

        // Assert
        await Assert.That(failure).IsNull();
    }

    [Test]
    public async Task A_security_kind_with_a_link_stops_the_host_at_startup()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<LinkedSecurityNotification>(TextsOf("linked_security", ("subject", "Hola"))));

        // Act
        var failure = await StartAsync(world);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure!.Message).Contains("linked_security");
            await Assert.That(failure.Message).Contains("must not carry a link");
        }
    }

    [Test]
    public async Task A_missing_text_stops_the_host_at_startup()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<MissingKeyNotification>(TextsOf("missing_key", ("subject", "Hola"))));

        // Act
        var failure = await StartAsync(world);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsTypeOf<KeyNotFoundException>();
            await Assert.That(failure!.Message).Contains("absent");
        }
    }

    [Test]
    public async Task A_missing_argument_stops_the_host_at_startup()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<MissingArgumentNotification>(TextsOf("missing_argument", ("subject", "Hola {Name}"))));

        // Act
        var failure = await StartAsync(world);

        // Assert
        await Assert.That(failure).IsTypeOf<Jeffijoe.MessageFormat.MessageFormatterException>();
    }

    [Test]
    public async Task A_kind_written_only_in_the_default_language_still_composes_in_every_language()
    {
        // Arrange
        var texts = new FakeTextSource().With("missing_key", "es-AR", ("subject", "Hola"), ("absent", "x"));
        await using var world = new EmailWorld(WithKind<MissingKeyNotification>(texts));

        // Act
        var failure = await StartAsync(world);

        // Assert
        await Assert.That(failure).IsNull();
    }

    [Test]
    public async Task A_draft_that_cannot_be_composed_for_its_payload_fails_for_good_and_sends_nothing()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<BadLinkNotification>(TextsOf("bad_link", ("subject", "Hola"))));

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("bad_link"), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected));
            await Assert.That(world.Sender.Sent).IsEmpty();
        }
    }

    [Test]
    public async Task A_security_kind_that_returns_a_standard_email_stops_the_host_at_startup()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<StandardSecurityNotification>(TextsOf("standard_security", ("subject", "Hola"))));

        // Act
        var failure = await StartAsync(world);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure!.Message).Contains("standard_security");
            await Assert.That(failure.Message).Contains("SecureEmail");
        }
    }

    [Test]
    public async Task A_security_kind_that_returns_a_standard_email_at_send_time_fails_for_good()
    {
        // Arrange
        await using var world = new EmailWorld(WithKind<StandardSecurityNotification>(TextsOf("standard_security", ("subject", "Hola"))));

        // Act
        var outcome = await world.Channel.DeliverAsync(world.ItemOf("standard_security"), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(outcome).IsEqualTo(new ChannelOutcome.Failed(DeliveryFailureReason.ProviderRejected));
            await Assert.That(world.Sender.Sent).IsEmpty();
        }
    }
}
