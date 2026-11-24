using Frappe.Identity.Contracts.Owners;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Kinds.Account;
using Frappe.Notifications.Producers.Identity;

namespace Frappe.Notifications.UnitTests.Producers;

/// <summary>What the Identity producer's mappers ask for, one per event (S18): pure functions of the event, nothing else.</summary>
public sealed class IdentityProducerMappersTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("email_verification", "account_activation_code", typeof(AccountActivationCodePayload))]
    [Arguments("password_reset", "password_reset_code", typeof(PasswordResetCodePayload))]
    [Arguments("email_change", "email_change_code", typeof(EmailChangeCodePayload))]
    public async Task A_code_issued_for_a_purpose_asks_for_the_code_mail_of_that_purpose(string purpose, string kind, Type payloadType)
    {
        // Arrange
        var fact = new OwnerCodeIssued(Guid.CreateVersion7(), purpose, "ana@example.com", "Ana", "es-AR", "482913", Now);

        // Act
        var produced = new OwnerCodeIssuedMapper().Map(fact);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(produced).Count().IsEqualTo(1);
            await Assert.That(produced[0].Kind).IsEqualTo(kind);
            await Assert.That(produced[0].Payload.GetType()).IsEqualTo(payloadType);
            await Assert.That(produced[0].Payload).IsEqualTo(Activator.CreateInstance(payloadType, "ana@example.com", "es-AR", "Ana", "482913"));
        }
    }

    [Test]
    public async Task A_code_issued_for_a_purpose_outside_the_closed_set_fails_without_echoing_the_address_or_the_code()
    {
        // Arrange
        var fact = new OwnerCodeIssued(Guid.CreateVersion7(), "mfa", "ana@example.com", "Ana", "es-AR", "482913", Now);

        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => new OwnerCodeIssuedMapper().Map(fact));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(thrown.Message).Contains("mfa");
            await Assert.That(thrown.Message).DoesNotContain("ana@example.com").And.DoesNotContain("482913");
        }
    }

    [Test]
    public async Task A_registration_attempt_asks_for_the_notice_to_the_real_owner()
    {
        // Arrange
        var fact = new OwnerRegistrationAttempted("ana@example.com", "pt-BR", Now);

        // Act
        var produced = new OwnerRegistrationAttemptedMapper().Map(fact);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(produced).Count().IsEqualTo(1);
            await Assert.That(produced[0].Kind).IsEqualTo("registration_attempted");
            await Assert.That(produced[0].Payload).IsEqualTo(new RegistrationAttemptedPayload("ana@example.com", "pt-BR"));
        }
    }

    [Test]
    public async Task An_email_change_request_asks_for_the_notice_to_the_old_address_naming_the_masked_new_one()
    {
        // Arrange
        var fact = new OwnerEmailChangeRequested(Guid.CreateVersion7(), "old@example.com", "n***@example.org", "en", "Ana", Now);

        // Act
        var produced = new OwnerEmailChangeRequestedMapper().Map(fact);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(produced).Count().IsEqualTo(1);
            await Assert.That(produced[0].Kind).IsEqualTo("email_change_requested");
            await Assert.That(produced[0].Payload)
                .IsEqualTo(new EmailChangeRequestedPayload("old@example.com", "en", "Ana", "n***@example.org"));
        }
    }

    [Test]
    public async Task A_confirmed_email_change_asks_for_the_undo_notice_to_the_old_address_with_the_console_link()
    {
        // Arrange
        var fact = new OwnerEmailChanged(Guid.CreateVersion7(), Now)
        {
            OldEmail = "old@example.com",
            UndoToken = "tok/en+1",
            Name = "Ana",
            Language = "en",
        };
        var options = Microsoft.Extensions.Options.Options.Create(
            new NotificationLinksOptions { ConsoleUrl = new Uri("https://console.example.com/app/") });

        // Act
        var produced = new OwnerEmailChangedMapper(options).Map(fact);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(produced).Count().IsEqualTo(1);
            await Assert.That(produced[0].Kind).IsEqualTo("email_changed");
            await Assert.That(produced[0].Payload).IsEqualTo(new EmailChangedPayload(
                "old@example.com", "en", "Ana", "https://console.example.com/app/undo-email-change?token=tok%2Fen%2B1"));
        }
    }

    [Test]
    public async Task An_email_change_published_before_the_undo_existed_asks_for_nothing()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(
            new NotificationLinksOptions { ConsoleUrl = new Uri("https://console.example.com/") });

        // Act
        var produced = new OwnerEmailChangedMapper(options).Map(new OwnerEmailChanged(Guid.CreateVersion7(), Now));

        // Assert
        await Assert.That(produced).IsEmpty();
    }

    [Test]
    [Arguments(null, false)]
    [Arguments("http://console.example.com/", false)]
    [Arguments("https://console.example.com/", true)]
    [Arguments("https://user:pw@console.example.com/", false)]
    public async Task The_console_url_must_be_an_absolute_https_url_without_credentials(string? url, bool valid)
    {
        // Arrange
        var options = new NotificationLinksOptions { ConsoleUrl = url is null ? null : new Uri(url) };

        // Act
        var result = new NotificationLinksOptionsValidator().Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsEqualTo(valid);
    }

    [Test]
    public async Task Every_event_of_the_producer_has_a_mapper_and_a_consumer_of_its_own_under_notifications_identity()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        // Act
        new IdentityProducerModule().AddServices(services);
        string[] consumers =
        [
            IdentityProducerModule.CodesConsumer, IdentityProducerModule.RegistrationAttemptedConsumer,
            IdentityProducerModule.EmailChangeRequestedConsumer, IdentityProducerModule.EmailChangedConsumer,
        ];

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(services.Any(d => d.ServiceType == typeof(INotificationProducer<OwnerCodeIssued>))).IsTrue();
            await Assert.That(services.Any(d => d.ServiceType == typeof(INotificationProducer<OwnerRegistrationAttempted>))).IsTrue();
            await Assert.That(services.Any(d => d.ServiceType == typeof(INotificationProducer<OwnerEmailChangeRequested>))).IsTrue();
            await Assert.That(consumers.Distinct().Count()).IsEqualTo(4);
            await Assert.That(RegisteredConsumer<OwnerCodeIssued>(services)).IsEqualTo(IdentityProducerModule.CodesConsumer);
            await Assert.That(RegisteredConsumer<OwnerRegistrationAttempted>(services))
                .IsEqualTo(IdentityProducerModule.RegistrationAttemptedConsumer);
            await Assert.That(RegisteredConsumer<OwnerEmailChangeRequested>(services))
                .IsEqualTo(IdentityProducerModule.EmailChangeRequestedConsumer);
            await Assert.That(RegisteredConsumer<OwnerEmailChanged>(services)).IsEqualTo(IdentityProducerModule.EmailChangedConsumer);
            await Assert.That(consumers).All().Satisfy(name => name.StartsWith("notifications-identity-", StringComparison.Ordinal));
        }
    }

    private static string? RegisteredConsumer<TEvent>(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
        services.Select(descriptor => descriptor.ImplementationInstance)
            .Where(instance => instance is not null
                && instance.GetType() is { IsGenericType: true } type
                && type.Name.StartsWith("IntegrationEventConsumerDeclaration", StringComparison.Ordinal)
                && type.GetGenericArguments()[1] == typeof(TEvent))
            .Select(instance => (string?)instance!.GetType().GetProperty("ConsumerName")!.GetValue(instance))
            .SingleOrDefault();
}
