using Frappe.Access.Http;
using Frappe.Access.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.UnitTests;

/// <summary>
/// The door of Access registers exactly what the hosts registered when they listed its facets themselves (S1): the Api the shared services and
/// the HTTP facet, the Worker the shared services and the system caller.
/// </summary>
public sealed class AccessModuleTests
{
    /// <summary>Every registration of <paramref name="register"/>, as service type, lifetime and what provides it, sorted.</summary>
    private static string[] Registrations(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return
        [
            .. services
                .Select(descriptor => $"{descriptor.ServiceType.FullName} {descriptor.Lifetime} "
                    + (descriptor.ImplementationType?.FullName ?? descriptor.ImplementationInstance?.GetType().FullName ?? "factory"))
                .Order(StringComparer.Ordinal),
        ];
    }

    [Test]
    public async Task The_Api_door_registers_the_shared_services_and_the_http_facet()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new AccessCoreServices().AddServices(services);
            new AccessHttp().AddServices(services);
        });

        // Act
        var door = Registrations(AccessModule.Api.AddServices);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(door).IsNotEmpty();
            await Assert.That(door).IsEquivalentTo(facets);
        }
    }

    [Test]
    public async Task The_Worker_door_registers_the_shared_services_and_the_system_caller()
    {
        // Arrange
        var facets = Registrations(services =>
        {
            new AccessCoreServices().AddServices(services);
            new AccessWorkerServices().AddServices(services);
        });

        // Act
        var door = Registrations(AccessModule.Worker.AddServices);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(door).IsNotEmpty();
            await Assert.That(door).IsEquivalentTo(facets);
        }
    }

    /// <summary>How many <c>IAfterCommitDomainEventHandler</c> registrations <paramref name="register"/> adds, by event type.</summary>
    private static string[] AfterCommitHandlers(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return [.. services
            .Where(descriptor => descriptor.ServiceType is { IsGenericType: true }
                && descriptor.ServiceType.GetGenericTypeDefinition().Name == "IAfterCommitDomainEventHandler`1")
            .GroupBy(descriptor => descriptor.ServiceType.GetGenericArguments()[0].Name)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} x{group.Count()}")];
    }

    [Test]
    public async Task The_Api_door_counts_after_the_commit_exactly_the_events_it_always_did()
    {
        // Act
        var handlers = AfterCommitHandlers(AccessModule.Api.AddServices);

        // Assert
        await Assert.That(handlers).IsEmpty();
    }

    [Test]
    public async Task The_Worker_door_counts_after_the_commit_exactly_the_events_it_always_did()
    {
        // Act
        var handlers = AfterCommitHandlers(AccessModule.Worker.AddServices);

        // Assert
        await Assert.That(handlers).IsEmpty();
    }
}
