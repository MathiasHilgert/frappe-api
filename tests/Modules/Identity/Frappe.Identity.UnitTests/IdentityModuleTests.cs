using Frappe.Aggregates;
using Frappe.Identity.Http;
using Frappe.Identity.Infrastructure;
using Frappe.Identity.Limits.Valkey;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.UnitTests;

/// <summary>
/// The door of Identity registers exactly what the hosts registered when they listed its facets themselves (S1): the Api the shared services,
/// the Api-only services, the Valkey limits and the HTTP facet; the Worker the shared services, the Worker-only services and the message purge
/// the Worker used to register itself.
/// </summary>
public sealed class IdentityModuleTests
{
    /// <summary>What the Worker registered for Identity's retention next to the module list, before the door took it over.</summary>
    private const string MessagePurge =
        "Frappe.Identity.Infrastructure.Retention.IMessagePurge Singleton Frappe.Identity.IdentityMessagePurge";

    /// <summary>Every registration of <paramref name="register"/>, as service type, key, lifetime and what provides it, sorted.</summary>
    private static string[] Registrations(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return [.. services.Select(Describe).Order(StringComparer.Ordinal)];
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
    public async Task The_Api_door_registers_the_shared_and_Api_services_the_valkey_limits_and_the_http_facet()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new IdentityCoreServices().AddServices(services);
            new IdentityApiServices().AddServices(services);
            new IdentityLimitsValkeyModule().AddServices(services);
            new IdentityHttp().AddServices(services);
        });

        // Act
        var door = Registrations(IdentityModule.Api.AddServices);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(door).IsNotEmpty();
            await Assert.That(door).IsEquivalentTo(facets);
            await Assert.That(door).DoesNotContain(MessagePurge);
        }
    }

    [Test]
    public async Task The_Worker_door_registers_the_shared_and_Worker_services_and_the_message_purge()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new IdentityCoreServices().AddServices(services);
            new IdentityWorkerServices().AddServices(services);
        });

        // Act
        var door = Registrations(IdentityModule.Worker.AddServices);

        // Assert
        await Assert.That(door).IsEquivalentTo([.. facets.Append(MessagePurge).Order(StringComparer.Ordinal)]);
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
        var handlers = AfterCommitHandlers(IdentityModule.Api.AddServices);

        // Assert
        await Assert.That(handlers).IsEquivalentTo(
            [
                "OwnerCodeIssued x1",
                "OwnerLoggedIn x1",
                "OwnerRegistered x1",
                "OwnerRegistrationAttempted x1",
                "OwnerVerified x1",
                "PasswordChanged x1",
                "PasswordReset x1",
                "SessionOpened x1",
                "SessionRevoked x1",
                "StepUpCompleted x1",
            ], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task The_Worker_door_counts_after_the_commit_exactly_the_events_it_always_did()
    {
        // Act
        var handlers = AfterCommitHandlers(IdentityModule.Worker.AddServices);

        // Assert
        await Assert.That(handlers).IsEmpty();
    }
}
