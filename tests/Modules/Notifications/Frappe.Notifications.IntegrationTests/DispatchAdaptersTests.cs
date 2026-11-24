using Frappe.Notifications.Domain.Deliveries;
using Frappe.Notifications.Domain.Preferences;
using Frappe.Notifications.Domain.Suppressions;
using Frappe.Notifications.Infrastructure.Dispatching;
using Frappe.Notifications.Infrastructure.Persistence;
using Frappe.Persistence;
using NodaTime;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>The EF adapters of the dispatch pipeline against a migrated database: staging that never saves, and the two gates' lookups.</summary>
[ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
[NotInParallel]
public sealed class DispatchAdaptersTests(PostgresServer server)
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 10, 0);

    private static readonly ChannelName Mail = ChannelName.Create("mail").Value;

    private static NotificationsDbContext Context(string owner) =>
        new(ModuleDatabase.Options<NotificationsDbContext>(owner, NotificationsDbContext.Schema, _ => { }));

    private static DeliveryTarget TargetOf(Guid message, AddressHash? hash = null) => new(
        message, "account_activation_code", NotificationCategory.Security, Mail,
        hash is null ? RecipientKey.Of(new ReferenceRecipient("account", "42")) : RecipientKey.Of(hash), hash);

    [Test]
    public async Task A_staged_delivery_exists_only_once_the_unit_of_work_saves_it()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var message = Guid.NewGuid();
            var target = TargetOf(message);
            await using var staging = Context(owner);
            var adapter = new EfDeliveryStaging(staging);

            // Act
            adapter.Stage(Delivery.Unresolved(Guid.CreateVersion7(), target, Now));
            await using var other = Context(owner);
            var beforeSave = await new EfDeliveryStaging(other).ExistsAsync(target, CancellationToken.None);
            await staging.SaveChangesAsync();
            var afterSave = await new EfDeliveryStaging(other).ExistsAsync(target, CancellationToken.None);
            var otherMessage = await new EfDeliveryStaging(other).ExistsAsync(TargetOf(Guid.NewGuid()), CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(beforeSave).IsFalse();
                await Assert.That(afterSave).IsTrue();
                await Assert.That(otherMessage).IsFalse();
            }
        });
    }

    [Test]
    public async Task A_suppression_matches_by_channel_hash_and_key_id()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var hash = new AddressHash(Enumerable.Repeat((byte)7, 32).ToArray(), 1);
            await using (var seed = Context(owner))
            {
                seed.Suppressions.Add(Suppression.Add(Mail, hash, SuppressionReason.HardBounce, SuppressionSource.Webhook, Now));
                await seed.SaveChangesAsync();
            }

            await using var db = Context(owner);
            var store = new EfSuppressionStore(db);

            // Act
            var same = await store.IsSuppressedAsync(Mail, [hash], CancellationToken.None);
            var amongKeys = await store.IsSuppressedAsync(Mail, [new AddressHash(new byte[32], 2), hash], CancellationToken.None);
            var otherKey = await store.IsSuppressedAsync(Mail, [new AddressHash(hash.Value, 2)], CancellationToken.None);
            var otherChannel = await store.IsSuppressedAsync(ChannelName.Create("text").Value, [hash], CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(same).IsTrue();
                await Assert.That(amongKeys).IsTrue();
                await Assert.That(otherKey).IsFalse();
                await Assert.That(otherChannel).IsFalse();
            }
        });
    }

    [Test]
    public async Task A_preference_matches_by_recipient_key_and_category()
    {
        await MigratedNotifications.RunAsync(server, async owner =>
        {
            // Arrange
            var key = RecipientKey.Of(new ReferenceRecipient("account", "42"));
            await using (var seed = Context(owner))
            {
                seed.Preferences.Add(Preference.OptOut(key, NotificationCategory.Marketing, Now).Value);
                await seed.SaveChangesAsync();
            }

            await using var db = Context(owner);
            var store = new EfPreferenceStore(db);

            // Act
            var optedOut = await store.IsOptedOutAsync(key, NotificationCategory.Marketing, CancellationToken.None);
            var otherRecipient = await store.IsOptedOutAsync(
                RecipientKey.Of(new ReferenceRecipient("account", "43")), NotificationCategory.Marketing, CancellationToken.None);

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(optedOut).IsTrue();
                await Assert.That(otherRecipient).IsFalse();
            }
        });
    }
}
