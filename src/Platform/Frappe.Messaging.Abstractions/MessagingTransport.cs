using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Messaging.Abstractions;

/// <summary>
/// The marker a transport (<c>AddJetStream</c>) leaves in the service collection to materialize declarations: the ones already
/// recorded when it attaches and every later one, so the order of the calls does not matter. A host without it records the
/// declarations and runs none of them.
/// </summary>
internal sealed class MessagingTransport(IMessagingDeclarationVisitor visitor)
{
    /// <summary>Attaches <paramref name="visitor"/> once per collection and materializes what was declared so far.</summary>
    public static void Attach(IServiceCollection services, IMessagingDeclarationVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(visitor);
        if (Find(services) is not null)
        {
            return;
        }

        services.AddSingleton(new MessagingTransport(visitor));
        foreach (var declaration in services.Select(d => d.ImplementationInstance).OfType<IMessagingDeclaration>().ToList())
        {
            declaration.Accept(visitor);
        }
    }

    /// <summary>Records <paramref name="declaration"/> and, when a transport is attached, materializes it now.</summary>
    public static void Declare(IServiceCollection services, IMessagingDeclaration declaration)
    {
        services.AddSingleton(declaration);
        Find(services)?.Materialize(declaration);
    }

    private static MessagingTransport? Find(IServiceCollection services) =>
        services.FirstOrDefault(d => d.ServiceType == typeof(MessagingTransport))?.ImplementationInstance as MessagingTransport;

    private void Materialize(IMessagingDeclaration declaration) => declaration.Accept(visitor);
}
