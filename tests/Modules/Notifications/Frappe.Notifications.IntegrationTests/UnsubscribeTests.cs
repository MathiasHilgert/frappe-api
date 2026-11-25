using System.Diagnostics.Metrics;
using System.Text.Json;
using Frappe.Notifications.Application.Deliveries;
using Frappe.Notifications.Application.Preferences;
using Frappe.Notifications.Application.Preferences.Unsubscribe;
using Frappe.Notifications.Contracts;
using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Infrastructure.Messaging;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Notifications.Infrastructure.Preferences;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Persistence;
using Frappe.Results;
using Frappe.Testing;
using Frappe.Testing.Containers;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The one-click unsubscribe against a migrated database, in the roles each host connects with (S5, S9, S15, N2-D2): the Api's role writes the
/// opt-out once however often or however many at once, counts it after the commit, and the Worker's role reads it, under every hash key.
/// </summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class UnsubscribeTests(PostgresServer server)
{
    private static readonly RecipientKey Ana = RecipientKey.Parse("ref:account:42").Value;

    private static readonly JsonElement MarketingPayload = JsonElement.Parse(
        """
        {"email":"ana@example.com","language":"es-AR","name":"Ana","headline":"Pagos",
         "summary":"Ya podes cobrar","url":"https://frappe.example/pagos"}
        """);

    private static readonly JsonElement SecurityPayload =
        JsonElement.Parse("""{"email":"ana@example.com","language":"es-AR","name":"Ana","code":"482913"}""");

    private static async Task DispatchAsync(DispatchWorld worker, string kind, JsonElement payload)
    {
        await using var scope = worker.Services.CreateAsyncScope();
        var step = ActivatorUtilities.CreateInstance<DispatchNotificationRequested>(scope.ServiceProvider);
        await step.HandleAsync(new NotificationRequested(Guid.CreateVersion7(), kind, payload), CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync();

        // The consumer's after-commit step: only now are the deliveries counted.
        scope.ServiceProvider.GetRequiredService<PendingDeliveries>().MarkClaimed();
        scope.ServiceProvider.GetRequiredService<PendingDeliveries>().CountIfClaimed();
    }

    private static async Task<Result<Unit>> UnsubscribeAsync(DispatchWorld api, string? token)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<Unsubscribe, Unit>>()
            .HandleAsync(new Unsubscribe(token), CancellationToken.None);
    }

    private static string TokenFor(DispatchWorld world, RecipientKey recipient) =>
        world.Services.GetRequiredService<IUnsubscribeTokens>().Issue(recipient, NotificationCategory.Marketing);

    private static NotificationsDbContext Context(string owner) =>
        new(ModuleDatabase.Options<NotificationsDbContext>(owner, NotificationsDbContext.Schema, _ => { }));

    private static async Task<long> CountAsync(string owner) =>
        (long)(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM notifications.preferences"))!;

    [Test]
    public async Task The_api_role_records_the_opt_out_once_however_often_the_link_is_used_and_counts_it_once()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);
            using var capture = new MetricCapture(api.Services.GetRequiredService<IMeterFactory>());
            var token = TokenFor(api, Ana);

            // Act
            var first = await UnsubscribeAsync(api, token);
            var repeat = await UnsubscribeAsync(api, token);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(first.IsSuccess).IsTrue();
                await Assert.That(repeat.IsSuccess).IsTrue();
                await Assert.That(await CountAsync(owner)).IsEqualTo(1);
                await Assert.That(await ScratchDatabase.ScalarAsync(owner, "SELECT recipient_key || '|' || category FROM notifications.preferences"))
                    .IsEqualTo("ref:account:42|marketing");
                await Assert.That(capture.Of("frappe.notifications.unsubscribes").Select(m => (string)m.Tags["category"]!))
                    .IsEquivalentTo(["marketing"]);
            }
        });
    }

    [Test]
    public async Task A_token_that_is_not_ours_writes_nothing_and_counts_nothing()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);
            using var capture = new MetricCapture(api.Services.GetRequiredService<IMeterFactory>());

            // Act
            var result = await UnsubscribeAsync(api, TokenFor(api, Ana) + "x");

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(result.IsSuccess).IsFalse();
                await Assert.That(result.Errors.Single().Code).IsEqualTo("notifications.unsubscribe_token.invalid");
                await Assert.That(await CountAsync(owner)).IsEqualTo(0);
                await Assert.That(capture.Of("frappe.notifications.unsubscribes")).IsEmpty();
            }
        });
    }

    [Test]
    public async Task The_same_link_used_twice_at_once_leaves_one_row_and_the_second_commit_succeeds()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: both read the recipient as still subscribed, then both write.
            await using var api = DispatchWorld.ForApi(owner);
            await using var first = api.Services.CreateAsyncScope();
            await using var second = api.Services.CreateAsyncScope();
            var now = Instant.FromUtc(2026, 11, 24, 10, 0);
            first.ServiceProvider.GetRequiredService<IPreferenceStore>().Stage(Preference.OptOut(Ana, NotificationCategory.Marketing, now).Value);
            second.ServiceProvider.GetRequiredService<IPreferenceStore>().Stage(Preference.OptOut(Ana, NotificationCategory.Marketing, now).Value);
            await first.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications").CommitAsync(CancellationToken.None);

            // Act
            await second.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("notifications").CommitAsync(CancellationToken.None);

            // Assert: the second commit drops its own row instead of failing, so the call that lost the race answers what a repeat answers.
            await Assert.That(await CountAsync(owner)).IsEqualTo(1);
        });
    }

    [Test]
    public async Task The_worker_role_reads_the_opt_out_for_marketing_and_still_lets_security_through()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            await using var api = DispatchWorld.ForApi(owner);
            await using var worker = DispatchWorld.ForWorker(owner);
            await UnsubscribeAsync(api, TokenFor(api, Ana));
            await using var scope = worker.Services.CreateAsyncScope();
            var gate = scope.ServiceProvider.GetRequiredService<PreferenceGate>();
            var address = new ContactRecipient(ChannelName.Create("email").Value, "ana@example.com");
            var contact = new ResolvedContact(address, Ana, new AddressHash(new byte[32], 1));
            var other = contact with { Key = RecipientKey.Parse("ref:account:43").Value };

            // Act
            var marketing = await gate.AllowsAsync(contact, NotificationCategory.Marketing, CancellationToken.None);
            var security = await gate.AllowsAsync(contact, NotificationCategory.Security, CancellationToken.None);
            var someoneElse = await gate.AllowsAsync(other, NotificationCategory.Marketing, CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(marketing).IsFalse();
                await Assert.That(security).IsTrue();
                await Assert.That(someoneElse).IsTrue();
            }
        });
    }

    [Test]
    public async Task An_opt_out_made_under_one_hash_key_is_found_when_the_recipient_is_probed_under_several()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var keyOne = RecipientKey.Of(new AddressHash(Enumerable.Repeat((byte)7, 32).ToArray(), 1));
            var keyTwo = RecipientKey.Of(new AddressHash(Enumerable.Repeat((byte)8, 32).ToArray(), 2));
            await using (var seed = Context(owner))
            {
                seed.Preferences.Add(Preference.OptOut(keyOne, NotificationCategory.Marketing, Instant.FromUtc(2026, 11, 24, 10, 0)).Value);
                await seed.SaveChangesAsync();
            }

            await using var db = Context(owner);
            var store = new EfPreferenceStore(db);

            // Act
            var amongKeys = await store.IsOptedOutAsync([keyTwo, keyOne], NotificationCategory.Marketing, CancellationToken.None);
            var onlyTheOther = await store.IsOptedOutAsync([keyTwo], NotificationCategory.Marketing, CancellationToken.None);
            var otherCategory = await store.IsOptedOutAsync([keyOne], NotificationCategory.Security, CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(amongKeys).IsTrue();
                await Assert.That(onlyTheOther).IsFalse();
                await Assert.That(otherCategory).IsFalse();
            }
        });
    }

    [Test]
    public async Task After_the_link_is_used_a_marketing_request_is_opted_out_and_counted_while_a_security_one_is_delivered()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange: a first marketing send shows the recipient key the worker derives for the address; its token is what the email carries.
            await using var api = DispatchWorld.ForApi(owner);
            await using var worker = DispatchWorld.ForWorker(
                owner, new Dictionary<string, string?>(), services => new MarketingMessagesModule().AddServices(services));
            using var capture = new MetricCapture(worker.Services.GetRequiredService<IMeterFactory>());
            await DispatchAsync(worker, "product_announcement", MarketingPayload);
            var stored = (string)(await ScratchDatabase.ScalarAsync(owner, "SELECT recipient_key FROM notifications.deliveries"))!;
            var key = RecipientKey.Parse(stored).Value;
            var unsubscribed = await UnsubscribeAsync(api, TokenFor(api, key));

            // Act
            await DispatchAsync(worker, "product_announcement", MarketingPayload);
            await DispatchAsync(worker, "account_activation_code", SecurityPayload);

            // Assert
            var outcomes = capture.Of("frappe.notifications.deliveries")
                .Select(m => ((string)m.Tags["kind"]!, (string)m.Tags["frappe.outcome"]!)).ToList();
            using (Assert.Multiple())
            {
                await Assert.That(unsubscribed.IsSuccess).IsTrue();
                await Assert.That(outcomes).IsEquivalentTo(
                    [("product_announcement", "sent"), ("product_announcement", "opted_out"), ("account_activation_code", "sent")]);
                await Assert.That((long)(await ScratchDatabase.ScalarAsync(owner, "SELECT count(*) FROM notifications.deliveries"))!).IsEqualTo(2);
            }
        });
    }
}
