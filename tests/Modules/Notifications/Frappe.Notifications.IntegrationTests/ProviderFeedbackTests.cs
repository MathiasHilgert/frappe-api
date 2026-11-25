using System.Diagnostics.Metrics;
using System.Text.Json;
using Frappe.Notifications.Application.Channels;
using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Application.Deliveries;
using Frappe.Notifications.Application.ProviderEvents;
using Frappe.Notifications.Application.ProviderEvents.RecordProviderFeedback;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.ProviderEvents;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using Frappe.Results;
using Frappe.Testing;
using Frappe.Testing.Containers;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The provider's feedback against a migrated database, in the roles each host connects with (S4, S15, Flow 3): the Worker records a delivery
/// and applies what was parked for it; the Api's webhook command dedupes, guards the transitions, parks, suppresses and counts after the
/// commit; a suppressed address is not sent to again.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class ProviderFeedbackTests(PostgresServer server)
{
    private const string Kind = "account_activation_code";

    private const string Address = "ana@example.com";

    private static readonly Instant At = Instant.FromUtc(2026, 11, 24, 10, 0);

    private static JsonElement Payload() =>
        JsonElement.Parse($$"""{"email":"{{Address}}","language":"es-AR","name":"Ana","code":"482913"}""");

    private static async Task DispatchAsync(DispatchWorld worker, Guid requestId)
    {
        await using var scope = worker.Services.CreateAsyncScope();
        var step = ActivatorUtilities.CreateInstance<DispatchNotificationRequested>(scope.ServiceProvider);
        await step.HandleAsync(new NotificationRequested(requestId, Kind, Payload()), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();
    }

    /// <summary>The message id the logging sender gives the delivery of this request: what the provider would call it.</summary>
    private static string MessageIdOf(DispatchWorld world, Guid requestId)
    {
        var hash = world.Services.GetRequiredService<IAddressHasher>().Hash(Address);
        var key = DeliveryIdempotency.Key(requestId, Kind, RecipientKey.Of(hash), ChannelName.Create("email").Value);
        return "logged-" + key[..16];
    }

    private static async Task<Result<Unit>> ReportAsync(
        DispatchWorld api, string eventId, string messageId, DeliveryFeedback feedback, params string[] addresses)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var report = new ProviderFeedback(
            DeliveryProvider.Resend, eventId, messageId, feedback, At, ChannelName.Create("email").Value,
            addresses.Length == 0 ? [Address] : addresses);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordProviderFeedback, Unit>>()
            .HandleAsync(new RecordProviderFeedback(report), CancellationToken.None);
    }

    private static async Task<string> StatusOfAsync(string owner, string messageId) =>
        (string)(await ScratchDatabase.ScalarAsync(owner, $"SELECT status FROM notifications.deliveries WHERE provider_message_id = '{messageId}'"))!;

    private static async Task<long> CountAsync(string owner, string table, string? where = null) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, $"SELECT count(*) FROM notifications.{table} {where}"))!;

    [Test]
    public async Task A_delivered_report_moves_the_sent_delivery_and_records_the_event_once_however_often_it_arrives()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);

            // Act
            var first = await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Delivered);
            var repeat = await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Delivered);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(first.IsSuccess).IsTrue();
                await Assert.That(repeat.IsSuccess).IsTrue();
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("delivered");
                await Assert.That(await CountAsync(owner, "provider_events")).IsEqualTo(1);
                await Assert.That(await CountAsync(owner, "provider_events", "WHERE matched")).IsEqualTo(1);
                await Assert.That(await CountAsync(owner, "suppressions")).IsEqualTo(0);
            }
        });
    }

    [Test]
    public async Task Events_out_of_order_a_delay_after_the_delivery_is_ignored_and_a_complaint_after_it_is_applied()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Delivered);

            // Act
            var delayed = await ReportAsync(api, "evt_2", messageId, DeliveryFeedback.Delayed);
            var afterDelay = await StatusOfAsync(owner, messageId);
            var complaint = await ReportAsync(api, "evt_3", messageId, DeliveryFeedback.Complaint);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(delayed.IsSuccess).IsTrue();
                await Assert.That(afterDelay).IsEqualTo("delivered");
                await Assert.That(complaint.IsSuccess).IsTrue();
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("complained");
                await Assert.That(await CountAsync(owner, "provider_events")).IsEqualTo(3);
            }
        });
    }

    [Test]
    public async Task A_webhook_before_the_delivery_is_parked_and_applied_when_the_Worker_records_the_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: the report arrives while the Worker has not yet committed the delivery it is about.
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            var messageId = MessageIdOf(worker, requestId);

            // Act
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Delivered);
            var parked = await CountAsync(
                owner, "provider_events", $"WHERE NOT matched AND provider_message_id = '{messageId}' AND feedback = 'delivered'");
            await DispatchAsync(worker, requestId);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(parked).IsEqualTo(1);
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("delivered");
                await Assert.That((bool)(await ScratchDatabase.ScalarAsync(
                    owner, "SELECT delivered_at IS NOT NULL FROM notifications.deliveries"))!).IsTrue();
            }
        });
    }

    private static async Task<Result<Unit>> ReceiveAsync(DispatchWorld api, string eventId, string messageId, DeliveryFeedback feedback)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var report = new ProviderFeedback(
            DeliveryProvider.Resend, eventId, messageId, feedback, At, ChannelName.Create("email").Value, [Address]);
        return await scope.ServiceProvider.GetRequiredService<ProviderFeedbackIntake>().RecordAsync(report, CancellationToken.None);
    }

    [Test]
    public async Task A_webhook_that_arrives_while_the_Worker_claim_is_open_waits_for_it_and_applies_to_the_committed_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: the Worker staged the delivery and its claim holds the message lock, uncommitted.
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            var messageId = MessageIdOf(worker, requestId);
            await using var scope = worker.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await ActivatorUtilities.CreateInstance<DispatchNotificationRequested>(scope.ServiceProvider)
                .HandleAsync(new NotificationRequested(requestId, Kind, Payload()), CancellationToken.None);
            await using var claim = await db.Database.BeginTransactionAsync();
            var inClaim = ActivatorUtilities.CreateInstance<ApplyParkedFeedback>(scope.ServiceProvider);
            await inClaim.HandleAsync(new NotificationRequested(requestId, Kind, Payload()), CancellationToken.None);

            // Act: the webhook arrives now and must wait; the claim commits; the webhook then finds the delivery.
            var webhook = ReceiveAsync(api, "evt_1", messageId, DeliveryFeedback.Delivered);
            await Task.Delay(500);
            var waiting = !webhook.IsCompleted;
            await db.SaveChangesAsync();
            await claim.CommitAsync();
            var result = await webhook.WaitAsync(TimeSpan.FromSeconds(30));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(waiting).IsTrue();
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("delivered");
                await Assert.That(await CountAsync(owner, "provider_events", "WHERE matched")).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_report_parked_after_the_before_claim_step_is_applied_by_the_claim_that_saves_the_delivery()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: the delivery is staged but not committed, so the webhook parks.
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            var messageId = MessageIdOf(worker, requestId);
            await using var scope = worker.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await ActivatorUtilities.CreateInstance<DispatchNotificationRequested>(scope.ServiceProvider)
                .HandleAsync(new NotificationRequested(requestId, Kind, Payload()), CancellationToken.None);
            await ReceiveAsync(api, "evt_1", messageId, DeliveryFeedback.Complaint);

            // Act: the claim runs its in-transaction step and commits.
            await using var claim = await db.Database.BeginTransactionAsync();
            await ActivatorUtilities.CreateInstance<ApplyParkedFeedback>(scope.ServiceProvider)
                .HandleAsync(new NotificationRequested(requestId, Kind, Payload()), CancellationToken.None);
            await db.SaveChangesAsync();
            await claim.CommitAsync();

            // Assert
            await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("complained");
        });
    }

    [Test]
    public async Task A_complaint_suppresses_the_address_and_the_next_send_to_it_is_not_made()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var firstRequest = Guid.CreateVersion7();
            await DispatchAsync(worker, firstRequest);
            var sentLogs = worker.Logs.Entries.Count(e => e.Message.Contains("was not sent", StringComparison.Ordinal));

            // Act
            await ReportAsync(api, "evt_1", MessageIdOf(worker, firstRequest), DeliveryFeedback.Complaint, "Ana@Example.com");
            await DispatchAsync(worker, Guid.CreateVersion7());

            // Assert
            var reason = await ScratchDatabase.ScalarAsync(owner, "SELECT reason FROM notifications.suppressions");
            var source = await ScratchDatabase.ScalarAsync(owner, "SELECT source FROM notifications.suppressions");
            using (Assert.Multiple())
            {
                await Assert.That(sentLogs).IsEqualTo(1);
                await Assert.That(reason).IsEqualTo("complaint");
                await Assert.That(source).IsEqualTo("webhook");
                await Assert.That(await CountAsync(owner, "suppressions")).IsEqualTo(1);
                await Assert.That(await CountAsync(owner, "deliveries")).IsEqualTo(1);
                await Assert.That(worker.Logs.Entries.Count(e => e.Message.Contains("was not sent", StringComparison.Ordinal))).IsEqualTo(1);
            }
        });
    }

    [Test]
    public async Task A_hard_bounce_and_the_provider_s_own_suppression_for_one_address_suppress_it_once()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);

            // Act
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.PermanentFailure);
            await ReportAsync(api, "evt_2", messageId, DeliveryFeedback.SuppressedByProvider);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("bounced");
                await Assert.That(await CountAsync(owner, "suppressions")).IsEqualTo(1);
                await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT reason FROM notifications.suppressions")).IsEqualTo("hard_bounce");
            }
        });
    }

    [Test]
    public async Task The_counters_count_each_recorded_event_and_each_suppression_once_after_the_commit_and_a_repeat_adds_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            using var capture = new MetricCapture(api.Services.GetRequiredService<IMeterFactory>());
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);

            // Act
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Complaint);
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Complaint);
            await ReportAsync(api, "evt_2", "re_unknown", DeliveryFeedback.Delivered);

            // Assert
            var events = capture.Of("frappe.notifications.provider_events").Select(m => $"{m.Tags["type"]}:{m.Tags["matched"]}");
            var suppressions = capture.Of("frappe.notifications.suppressions").Select(m => (string)m.Tags["reason"]!);
            using (Assert.Multiple())
            {
                await Assert.That(events).IsEquivalentTo(["complaint:True", "delivered:False"]);
                await Assert.That(suppressions).IsEquivalentTo(["complaint"]);
            }
        });
    }

    [Test]
    public async Task The_first_delivered_report_records_the_time_from_sent_to_delivered_once_after_the_commit()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            using var capture = new MetricCapture(api.Services.GetRequiredService<IMeterFactory>());
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);

            // Act
            await ReportAsync(api, "evt_1", messageId, DeliveryFeedback.Delayed);
            await ReportAsync(api, "evt_2", messageId, DeliveryFeedback.Delivered);
            await ReportAsync(api, "evt_2", messageId, DeliveryFeedback.Delivered);
            await ReportAsync(api, "evt_3", messageId, DeliveryFeedback.Delivered);

            // Assert
            var measurement = capture.Of("frappe.notifications.delivery.time_to_delivered").Single();
            using (Assert.Multiple())
            {
                await Assert.That(measurement.Value).IsGreaterThanOrEqualTo(0);
                await Assert.That(measurement.Tags["kind"]).IsEqualTo(Kind);
                await Assert.That(measurement.Tags["channel"]).IsEqualTo("email");
            }
        });
    }

    [Test]
    public async Task Two_writers_that_record_the_same_event_or_move_the_same_delivery_at_once_leave_the_loser_a_conflict_and_write_nothing_twice()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var worker = DispatchWorld.ForWorker(owner);
            await using var api = DispatchWorld.ForApi(owner);
            var requestId = Guid.CreateVersion7();
            await DispatchAsync(worker, requestId);
            var messageId = MessageIdOf(worker, requestId);
            await using var first = api.Services.CreateAsyncScope();
            await using var second = api.Services.CreateAsyncScope();
            var firstUnit = first.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications");
            var secondUnit = second.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications");
            var firstDelivery = await first.ServiceProvider.GetRequiredService<IDeliveries>()
                .FindByProviderMessageAsync(DeliveryProvider.Resend, messageId, CancellationToken.None);
            var secondDelivery = await second.ServiceProvider.GetRequiredService<IDeliveries>()
                .FindByProviderMessageAsync(DeliveryProvider.Resend, messageId, CancellationToken.None);

            // Act: both read the delivery as sent; one moves it to delivered and commits, the other to bounced.
            _ = secondDelivery!.Apply(DeliveryFeedback.Delivered, At);
            await secondUnit.CommitAsync(CancellationToken.None);
            _ = firstDelivery!.Apply(DeliveryFeedback.PermanentFailure, At);
            var conflict = await Assert.ThrowsAsync<CommitConflictException>(() => firstUnit.CommitAsync(CancellationToken.None));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(conflict).IsNotNull();
                await Assert.That(await StatusOfAsync(owner, messageId)).IsEqualTo("delivered");
            }
        });
    }

    [Test]
    public async Task The_same_event_recorded_by_two_writers_at_once_is_a_conflict_for_the_second()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);
            await using var first = api.Services.CreateAsyncScope();
            await using var second = api.Services.CreateAsyncScope();
            first.ServiceProvider.GetRequiredService<IProviderEvents>()
                .Stage(ProviderEvent.Handled(DeliveryProvider.Resend, "evt_1", DeliveryFeedback.Delivered, matched: false, At));
            second.ServiceProvider.GetRequiredService<IProviderEvents>()
                .Stage(ProviderEvent.Handled(DeliveryProvider.Resend, "evt_1", DeliveryFeedback.Delivered, matched: false, At));
            await first.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications").CommitAsync(CancellationToken.None);

            // Act
            var conflict = await Assert.ThrowsAsync<CommitConflictException>(
                () => second.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications").CommitAsync(CancellationToken.None));

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(conflict).IsNotNull();
                await Assert.That(await CountAsync(owner, "provider_events")).IsEqualTo(1);
            }
        });
    }
}
