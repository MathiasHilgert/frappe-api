using Frappe.Identity.Contracts.Owners;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Abstractions;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Producers.Identity;
using Frappe.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The Identity producer end to end against a migrated database, as the Worker's role (S18): an Identity event that reaches the inbox becomes
/// one sent delivery of the kind its mapper names, under the message's id, without the address or the code in the logs.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class IdentityProducerTests(PostgresServer server)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private static DispatchWorld Worker(string owner) => DispatchWorld.ForWorker(
        owner, new Dictionary<string, string?>(), services => new IdentityProducerModule().AddServices(services));

    private static async Task ConsumeAsync<TEvent>(DispatchWorld worker, Guid messageId, TEvent fact)
        where TEvent : IIntegrationEvent
    {
        await using var scope = worker.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentMessage>().WithId(messageId);
        await ActivatorUtilities.CreateInstance<DispatchProducedNotifications<TEvent>>(scope.ServiceProvider)
            .HandleAsync(fact, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();
    }

    private static async Task<long> DeliveriesAsync(string owner, string where) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM notifications.deliveries {where}"))!;

    [Test]
    [Arguments("email_verification", "account_activation_code")]
    [Arguments("password_reset", "password_reset_code")]
    [Arguments("email_change", "email_change_code")]
    public async Task A_code_issued_event_sends_the_code_mail_of_its_purpose(string purpose, string kind)
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var messageId = Guid.CreateVersion7();

            // Act
            await ConsumeAsync(
                worker, messageId, new OwnerCodeIssued(Guid.CreateVersion7(), purpose, "ana@example.com", "Ana", "es-AR", "482913", Now));

            // Assert
            var logged = string.Join('\n', worker.Logs.Entries.Select(e => e.Message));
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(owner, $"WHERE source_message_id = '{messageId}' AND kind = '{kind}' AND status = 'sent'"))
                    .IsEqualTo(1);
                await Assert.That(logged).DoesNotContain("ana@example.com").And.DoesNotContain("482913");
            }
        });
    }

    [Test]
    public async Task A_registration_attempt_sends_the_notice_to_the_real_owner()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var messageId = Guid.CreateVersion7();

            // Act
            await ConsumeAsync(worker, messageId, new OwnerRegistrationAttempted("ana@example.com", "en", Now));

            // Assert
            var logged = string.Join('\n', worker.Logs.Entries.Select(e => e.Message));
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(
                    owner, $"WHERE source_message_id = '{messageId}' AND kind = 'registration_attempted' AND status = 'sent'")).IsEqualTo(1);
                await Assert.That(logged).DoesNotContain("ana@example.com");
            }
        });
    }

    [Test]
    public async Task An_email_change_request_sends_the_notice_to_the_old_address()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var messageId = Guid.CreateVersion7();

            // Act
            await ConsumeAsync(
                worker, messageId, new OwnerEmailChangeRequested(Guid.CreateVersion7(), "old@example.com", "n***@example.org", "pt-BR", "Ana", Now));

            // Assert
            var logged = string.Join('\n', worker.Logs.Entries.Select(e => e.Message));
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(
                    owner, $"WHERE source_message_id = '{messageId}' AND kind = 'email_change_requested' AND status = 'sent'")).IsEqualTo(1);
                await Assert.That(logged).DoesNotContain("old@example.com").And.DoesNotContain("example.org");
            }
        });
    }

    [Test]
    public async Task A_code_for_a_purpose_outside_the_closed_set_throws_and_sends_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);

            // Act and assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => ConsumeAsync(
                worker, Guid.CreateVersion7(), new OwnerCodeIssued(Guid.CreateVersion7(), "mfa", "ana@example.com", "Ana", "es-AR", "482913", Now)));
            await Assert.That(await DeliveriesAsync(owner, string.Empty)).IsEqualTo(0);
        });
    }
}
