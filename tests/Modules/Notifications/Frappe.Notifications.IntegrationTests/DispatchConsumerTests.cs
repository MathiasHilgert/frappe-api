using System.Text.Json;
using Frappe.Notifications.Application.Requests.SendNotification;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Results;
using Frappe.Testing.Containers;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The explicit path end to end against a migrated database, in the roles each host connects with (S2, S14, S16): the command accepts a
/// request and its event reaches the outbox; the dispatch step, as the Worker, sends it through the logging sender and records one
/// delivery, once, however many times the message is redelivered.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class DispatchConsumerTests(PostgresServer server)
{
    private const string Kind = "account_activation_code";

    private static JsonElement Payload() =>
        JsonElement.Parse("""{"email":"ana@example.com","language":"es-AR","name":"Ana","code":"482913"}""");

    private static async Task<Result<Unit>> SendAsync(DispatchWorld api, Guid requestId, string kind = Kind)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<SendNotification, Unit>>()
            .HandleAsync(new SendNotification(requestId, kind, Payload()), CancellationToken.None);
    }

    private static async Task DispatchAsync(DispatchWorld worker, NotificationRequested message)
    {
        // The consumer's claim: the before-claim step stages the deliveries and the claim's save commits them.
        await using var scope = worker.Services.CreateAsyncScope();
        var step = ActivatorUtilities.CreateInstance<DispatchNotificationRequested>(scope.ServiceProvider);
        await step.HandleAsync(message, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();
    }

    private static async Task<long> CountAsync(string owner, string table, string? where = null) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM notifications.{table} {where}"))!;

    [Test]
    public async Task An_accepted_request_stages_one_request_row_and_one_outbox_event_with_its_payload_and_a_retry_adds_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();

            // Act
            var first = await SendAsync(api, requestId);
            var retry = await SendAsync(api, requestId);

            // Assert
            var type = (string?)await ScratchDatabase.ScalarAsync(owner, "SELECT type FROM notifications.outbox_messages");
            var data = (string?)await ScratchDatabase.ScalarAsync(owner, "SELECT data FROM notifications.outbox_messages");
            var source = (string?)await ScratchDatabase.ScalarAsync(owner, "SELECT source FROM notifications.outbox_messages");
            using var json = JsonDocument.Parse(data!);
            using (Assert.Multiple())
            {
                await Assert.That(first.IsSuccess).IsTrue();
                await Assert.That(retry.IsSuccess).IsTrue();
                await Assert.That(await CountAsync(owner, "send_requests", $"WHERE id = '{requestId}' AND kind = '{Kind}'")).IsEqualTo(1);
                await Assert.That(await CountAsync(owner, "outbox_messages")).IsEqualTo(1);
                await Assert.That(type).IsEqualTo("frappe.notifications.notification_requested.v1");
                await Assert.That(source).IsEqualTo("/frappe/notifications");
                await Assert.That(json.RootElement.GetProperty("requestId").GetGuid()).IsEqualTo(requestId);
                await Assert.That(json.RootElement.GetProperty("kind").GetString()).IsEqualTo(Kind);
                await Assert.That(json.RootElement.GetProperty("payload").GetProperty("code").GetString()).IsEqualTo("482913");
            }
        });
    }

    [Test]
    public async Task A_refused_request_stages_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);

            // Act
            var unknown = await SendAsync(api, Guid.CreateVersion7(), "nope");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(unknown.Errors.Single().Code).IsEqualTo("notifications.kind.unknown");
                await Assert.That(await CountAsync(owner, "send_requests")).IsEqualTo(0);
                await Assert.That(await CountAsync(owner, "outbox_messages")).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task The_dispatch_sends_through_the_logging_sender_records_one_delivery_and_logs_nothing_personal()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            var requestId = Guid.CreateVersion7();

            // Act
            await DispatchAsync(worker, new NotificationRequested(requestId, Kind, Payload()));

            // Assert
            var row = await ReadDeliveryAsync(owner);
            var logged = string.Join('\n', worker.Logs.Entries.Select(e => e.Message));
            using (Assert.Multiple())
            {
                await Assert.That(row.Source).IsEqualTo(requestId);
                await Assert.That(row.Kind).IsEqualTo(Kind);
                await Assert.That(row.Channel).IsEqualTo("email");
                await Assert.That(row.Status).IsEqualTo("sent");
                await Assert.That(row.Provider).IsEqualTo("resend");
                await Assert.That(row.ProviderMessageId).StartsWith("logged-");
                await Assert.That(row.RecipientKey).StartsWith("addr:1:");
                await Assert.That(row.Locale).IsEqualTo("es-AR");
                await Assert.That(logged).Contains("was not sent").And.DoesNotContain("ana@example.com").And.DoesNotContain("482913");
            }
        });
    }

    [Test]
    public async Task A_redelivered_message_records_once_and_sends_once()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            var message = new NotificationRequested(Guid.CreateVersion7(), Kind, Payload());

            // Act
            await DispatchAsync(worker, message);
            await DispatchAsync(worker, message);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(1);
                await Assert.That(worker.Logs.Entries.Count(e => e.Message.Contains("was not sent", StringComparison.Ordinal))).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task Two_messages_of_one_request_collapse_into_one_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: an HTTP retry that slipped past the Api's own idempotency produced a second message with the same request id.
            await using var worker = DispatchWorld.ForWorker(owner);
            var requestId = Guid.CreateVersion7();

            // Act
            await DispatchAsync(worker, new NotificationRequested(requestId, Kind, Payload()));
            await DispatchAsync(worker, new NotificationRequested(requestId, Kind, Payload()));

            // Assert
            await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(1);
        });
    }

    [Test]
    public async Task A_kind_this_host_does_not_know_throws_so_the_message_is_retried_and_stages_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);

            // Act and assert
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                () => DispatchAsync(worker, new NotificationRequested(Guid.CreateVersion7(), "nope", Payload())));
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).Contains("nope").And.DoesNotContain("ana@example.com");
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task A_payload_that_is_not_the_kinds_throws_without_echoing_it()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);

            // Act and assert
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DispatchAsync(
                worker, new NotificationRequested(Guid.CreateVersion7(), Kind, JsonElement.Parse("""{"email":"ana@example.com"}"""))));
            using (Assert.Multiple())
            {
                await Assert.That(thrown!.Message).DoesNotContain("ana@example.com");
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(0);
            }
        });
    }

    private static async Task<(Guid Source, string Kind, string Channel, string Status, string Provider, string ProviderMessageId,
        string RecipientKey, string Locale)> ReadDeliveryAsync(string owner)
    {
        await using var connection = new NpgsqlConnection(owner);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT source_message_id, kind, channel, status, provider, provider_message_id, recipient_key, locale_served "
            + "FROM notifications.deliveries",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetString(6), reader.GetString(7));
    }
}
