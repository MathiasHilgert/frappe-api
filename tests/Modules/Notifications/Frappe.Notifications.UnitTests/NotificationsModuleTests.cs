using Frappe.Aggregates;
using Frappe.Notifications.Http;
using Frappe.Notifications.Infrastructure;
using Frappe.Notifications.Messages.Account;
using Frappe.Notifications.Messages.Marketing;
using Frappe.Notifications.Senders.Email;
using Frappe.Notifications.Triggers.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Notifications.UnitTests;

/// <summary>
/// The door of Notifications registers exactly what the hosts registered when they listed its facets and plug-ins themselves (S1), in the
/// same order: the Api the shared services, the kinds, the email channel's Api side and the HTTP facet; the Worker the shared services, the
/// Worker-only services, the kinds, the Identity producer, the email channel's Worker side and the message purge the Worker used to register
/// itself.
/// </summary>
public sealed class NotificationsModuleTests
{
    /// <summary>What the Worker registered for the Notifications purge after the module list, before the door took it over.</summary>
    private const string MessagePurge =
        "Frappe.Notifications.Infrastructure.Retention.IMessagePurge Singleton Frappe.Notifications.NotificationsMessagePurge";

    /// <summary>Every registration of <paramref name="register"/>, as service type, key, lifetime and what provides it, in order.</summary>
    private static string[] Registrations(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return [.. services.Select(Describe)];
    }

    private static string Describe(ServiceDescriptor descriptor)
    {
        var provider = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType?.FullName ?? descriptor.KeyedImplementationInstance?.GetType().FullName ?? "factory"
            : descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "factory";
        var key = descriptor.IsKeyedService ? $" [{descriptor.ServiceKey}]" : string.Empty;
        return $"{descriptor.ServiceType.FullName}{key} {descriptor.Lifetime} {provider}";
    }

    [Test]
    public async Task The_Api_door_registers_the_shared_services_the_kinds_the_email_webhooks_and_the_http_facet_in_order()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new NotificationsCoreServices().AddServices(services);
            new AccountMessagesModule().AddServices(services);
            new MarketingMessagesModule().AddServices(services);
            new EmailSenderApiModule().AddServices(services);
            new NotificationsHttp().AddServices(services);
        });

        // Act
        var door = Registrations(NotificationsModule.Api.AddServices);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(door).IsNotEmpty();
            await Assert.That(door).IsEquivalentTo(facets, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(door).DoesNotContain(MessagePurge);
        }
    }

    [Test]
    public async Task The_Worker_door_registers_the_shared_and_Worker_services_the_plug_ins_and_the_message_purge_in_order()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new NotificationsCoreServices().AddServices(services);
            new NotificationsWorkerServices().AddServices(services);
            new AccountMessagesModule().AddServices(services);
            new MarketingMessagesModule().AddServices(services);
            new IdentityTriggersModule().AddServices(services);
            new EmailSenderWorkerModule().AddServices(services);
        });

        // Act
        var door = Registrations(NotificationsModule.Worker.AddServices);

        // Assert
        await Assert.That(door).IsEquivalentTo([.. facets, MessagePurge], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    /// <summary>How many <c>IAfterCommitDomainEventHandler</c> registrations <paramref name="register"/> adds, by event type.</summary>
    private static string[] AfterCommitHandlers(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return [.. services
            .Where(descriptor => descriptor.ServiceType is { IsGenericType: true }
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IAfterCommitDomainEventHandler<>))
            .GroupBy(descriptor => descriptor.ServiceType.GetGenericArguments()[0].Name)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} x{group.Count()}")];
    }

    [Test]
    public async Task The_Api_door_counts_after_the_commit_exactly_the_events_it_always_did()
    {
        // Act
        var handlers = AfterCommitHandlers(NotificationsModule.Api.AddServices);

        // Assert
        await Assert.That(handlers).IsEquivalentTo(
            [
                "DeliveryDelivered x1",
                "PreferenceOptedOut x1",
                "ProviderEventRecorded x1",
                "SuppressionAdded x1",
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Worker_door_counts_after_the_commit_exactly_the_events_it_always_did()
    {
        // Act
        var handlers = AfterCommitHandlers(NotificationsModule.Worker.AddServices);

        // Assert
        await Assert.That(handlers).IsEquivalentTo(
            [
                "DeliveryDelivered x1",
                "PreferenceOptedOut x1",
                "ProviderEventRecorded x1",
                "SuppressionAdded x1",
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
