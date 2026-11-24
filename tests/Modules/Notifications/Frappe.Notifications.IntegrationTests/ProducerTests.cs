using Frappe.IntegrationEvents;
using Frappe.Messaging.Abstractions;
using Frappe.Notifications.Application.Producers;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Kinds.Account;
using Frappe.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The producer seam end to end against a migrated database, as the Worker's role (S2a, S14): a fact another module publishes is mapped by the
/// plug-in's mapper and becomes a delivery, once, however many times the message is redelivered. The producer here is test-only (DES-09).
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class ProducerTests(PostgresServer server)
{
    private const string Consumer = "notifications-orders-confirmation";

    private static DispatchWorld Worker(string owner) => DispatchWorld.ForWorker(
        owner,
        new Dictionary<string, string?>(),
        services => services.AddNotificationProducer<OrderPlaced, OrderPlacedMapper>(Consumer));

    /// <summary>The consumer's claim for one message: the before-claim step of the declaration, then the claim's save.</summary>
    private static async Task ConsumeAsync(DispatchWorld worker, Guid messageId, OrderPlaced fact)
    {
        await using var scope = worker.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentMessage>().WithId(messageId);
        await ActivatorUtilities.CreateInstance<DispatchProducedNotifications<OrderPlaced>>(scope.ServiceProvider)
            .HandleAsync(fact, CancellationToken.None);
        await ActivatorUtilities.CreateInstance<ApplyParkedFeedbackFor<OrderPlaced>>(scope.ServiceProvider)
            .HandleAsync(fact, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();
    }

    private static async Task<long> DeliveriesAsync(string owner, string? where = null) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM notifications.deliveries {where}"))!;

    [Test]
    public async Task A_fact_of_another_module_becomes_one_delivery_of_the_mapped_kind_under_the_messages_id()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var messageId = Guid.CreateVersion7();

            // Act
            await ConsumeAsync(worker, messageId, new OrderPlaced("ana@example.com", "482913"));

            // Assert
            var logged = string.Join('\n', worker.Logs.Entries.Select(e => e.Message));
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(1);
                await Assert.That(await DeliveriesAsync(owner, $"WHERE source_message_id = '{messageId}' AND kind = 'account_activation_code'"))
                    .IsEqualTo(1);
                await Assert.That(await DeliveriesAsync(owner, "WHERE channel = 'email' AND status = 'sent'")).IsEqualTo(1);
                await Assert.That(logged).DoesNotContain("ana@example.com").And.DoesNotContain("482913");
            }
        });
    }

    [Test]
    public async Task A_redelivered_message_sends_and_records_once_and_a_second_message_is_a_second_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            var messageId = Guid.CreateVersion7();
            var fact = new OrderPlaced("ana@example.com", "482913");

            // Act
            await ConsumeAsync(worker, messageId, fact);
            await ConsumeAsync(worker, messageId, fact);
            await ConsumeAsync(worker, Guid.CreateVersion7(), fact);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(2);
                await Assert.That(worker.Logs.Entries.Count(e => e.Message.Contains("was not sent", StringComparison.Ordinal))).IsEqualTo(2);
            }
        });
    }

    [Test]
    public async Task A_fact_that_maps_the_same_notification_twice_sends_and_records_it_once()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);

            // Act
            await ConsumeAsync(worker, Guid.CreateVersion7(), new OrderPlaced("twice@example.com", "482913"));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(1);
                await Assert.That(worker.Logs.Entries.Count(e => e.Message.Contains("was not sent", StringComparison.Ordinal))).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_fact_that_asks_for_no_notification_records_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);

            // Act
            await ConsumeAsync(worker, Guid.CreateVersion7(), new OrderPlaced(string.Empty, string.Empty));

            // Assert
            await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(0);
        });
    }

    [Test]
    public async Task A_mapper_that_names_a_kind_this_host_does_not_have_throws_so_the_message_is_retried()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);

            // Act and assert
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ConsumeAsync(worker, Guid.CreateVersion7(), new OrderPlaced("nope@example.com", "000000")));
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).Contains("not_a_kind").And.DoesNotContain("nope@example.com");
                await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_mapper_that_gives_a_kind_the_wrong_payload_throws_without_echoing_it()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);

            // Act and assert
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ConsumeAsync(worker, Guid.CreateVersion7(), new OrderPlaced("wrong@example.com", "000000")));
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).Contains("account_activation_code").And.DoesNotContain("wrong@example.com");
                await Assert.That(await DeliveriesAsync(owner)).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_fact_handled_outside_a_consumer_has_no_message_id_to_send_under_and_throws()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = Worker(owner);
            await using var scope = worker.Services.CreateAsyncScope();
            var step = ActivatorUtilities.CreateInstance<DispatchProducedNotifications<OrderPlaced>>(scope.ServiceProvider);

            // Act and assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => step.HandleAsync(new OrderPlaced("ana@example.com", "482913"), CancellationToken.None));
        });
    }

    /// <summary>A fact of a test-only module, as another module would publish it.</summary>
    [IntegrationEvent("frappe.orders.order_placed.v1")]
    public sealed record OrderPlaced(string Email, string Code) : IIntegrationEvent;

    /// <summary>The test producer's mapper: a placed order asks for an activation code, and the e-mail picks what the test needs.</summary>
    public sealed class OrderPlacedMapper : INotificationProducer<OrderPlaced>
    {
        public IReadOnlyList<ProducedNotification> Map(OrderPlaced integrationEvent) => integrationEvent.Email switch
        {
            "" => [],
            "twice@example.com" => [Activation(integrationEvent), Activation(integrationEvent)],
            "nope@example.com" => [new ProducedNotification("not_a_kind", new object())],
            "wrong@example.com" => [new ProducedNotification(AccountActivationCode.Kind, new object())],
            _ => [Activation(integrationEvent)],
        };

        private static ProducedNotification Activation(OrderPlaced integrationEvent) =>
            ProducedNotification.Of<AccountActivationCode, AccountActivationCodePayload>(
                new AccountActivationCodePayload(integrationEvent.Email, "es-AR", "Ana", integrationEvent.Code));
    }
}
